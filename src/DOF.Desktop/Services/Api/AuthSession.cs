using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services.Security;

namespace DOF.Desktop.Services.Api;

public sealed record AuthResult(bool Succeeded, string? Error)
{
    public static AuthResult Success { get; } = new(true, null);

    public static AuthResult Failure(string error) => new(false, error);
}

/// <summary>
/// The desktop client's sign-in session. The access token is held in memory only and refreshed shortly
/// before it expires; the refresh token is kept in the operating system's secure store so the user stays
/// signed in across restarts. Network failures propagate (the stored session is kept for later); a rejected
/// session clears the stored token.
/// </summary>
public sealed class AuthSession(HttpClient http, ISecureStore store, TimeProvider? clock = null) : IDisposable
{
    public const string RefreshTokenKey = "refresh-token";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public CurrentUserResponse? CurrentUser { get; private set; }

    public bool IsSignedIn => CurrentUser is not null;

    public event EventHandler? SessionChanged;

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var response = await http.PostAsJsonAsync("api/v1/auth/login",
                new LoginRequest(email, password, Environment.MachineName), ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest
                or HttpStatusCode.TooManyRequests)
            {
                return AuthResult.Failure(await ReadProblemAsync(response, ct));
            }

            response.EnsureSuccessStatusCode();
            await AcceptTokensAsync(response, ct);
            return AuthResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Signs in silently using the stored refresh token. Returns false if there is no valid stored session.</summary>
    public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await RefreshCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A valid access token for API calls, refreshing it if needed; null if the session has ended.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt - _clock.GetUtcNow() > RefreshMargin)
            {
                return _accessToken;
            }

            return await RefreshCoreAsync(ct) ? _accessToken : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Revokes the session on the server (best effort when offline) and always clears it locally.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var refreshToken = store.Get(RefreshTokenKey);
            if (refreshToken is not null)
            {
                try
                {
                    using var response = await http.PostAsJsonAsync("api/v1/auth/logout", new LogoutRequest(refreshToken), ct);
                }
                catch (HttpRequestException)
                {
                    // Offline: the server-side token will expire on its own; local sign-out must still happen.
                }
            }

            store.Remove(RefreshTokenKey);
            ClearSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<bool> RefreshCoreAsync(CancellationToken ct)
    {
        var refreshToken = store.Get(RefreshTokenKey);
        if (refreshToken is null)
        {
            ClearSession();
            return false;
        }

        using var response = await http.PostAsJsonAsync("api/v1/auth/refresh", new RefreshTokenRequest(refreshToken), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            store.Remove(RefreshTokenKey);
            ClearSession();
            return false;
        }

        response.EnsureSuccessStatusCode();
        await AcceptTokensAsync(response, ct);
        return true;
    }

    private async Task AcceptTokensAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthTokensResponse>>(ct);
        var tokens = body?.Data ?? throw new InvalidOperationException("The server returned an empty sign-in response.");

        store.Set(RefreshTokenKey, tokens.RefreshToken);
        _accessToken = tokens.AccessToken;
        _accessTokenExpiresAt = tokens.AccessTokenExpiresAt;
        CurrentUser = tokens.User;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSession()
    {
        var wasSignedIn = IsSignedIn;
        _accessToken = null;
        _accessTokenExpiresAt = default;
        CurrentUser = null;
        if (wasSignedIn)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return "Too many sign-in attempts. Please wait a minute and try again.";
        }

        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { } message)
            {
                return message;
            }
        }
        catch (JsonException)
        {
        }

        return response.StatusCode == HttpStatusCode.BadRequest
            ? "Please enter your email and password."
            : "Invalid email or password.";
    }
}
