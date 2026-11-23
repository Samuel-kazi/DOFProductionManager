using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DOF.Application.Activity;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.GoogleCalendar;

/// <summary>
/// Connects the organisation's Google account (settings.manage). The desktop runs the browser sign-in
/// against a loopback address; the server keeps the PKCE verifier inside an encrypted, short-lived
/// <c>state</c> value, exchanges the code itself and stores only the encrypted refresh token.
/// </summary>
public sealed class GoogleConnectionService(
    IApplicationDbContext db,
    IGoogleCalendarApi google,
    ISecretProtector protector,
    ICurrentUser currentUser,
    ActivityRecorder activity,
    TimeProvider clock,
    ILogger<GoogleConnectionService> logger)
{
    public const string RefreshTokenPurpose = "google-refresh-token";
    private const string StatePurpose = "google-oauth-state";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public bool IsConfigured => google.IsConfigured && protector.IsConfigured;

    public Task<GoogleCalendarConnection?> ActiveAsync(CancellationToken ct) =>
        db.GoogleCalendarConnections.SingleOrDefaultAsync(c => c.ArchivedAt == null, ct);

    public async Task<GoogleCalendarStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var canManage = currentUser.HasPermission(Permissions.SettingsManage);
        var active = await db.GoogleCalendarConnections.AsNoTracking()
            .Where(c => c.ArchivedAt == null)
            .Select(c => new
            {
                c.AccountEmail, c.CreatedAt, c.LastError,
                ConnectedByName = db.Users.Where(u => u.Id == c.ConnectedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        return active is null
            ? new GoogleCalendarStatusResponse(IsConfigured, false, null, null, null, null, canManage)
            : new GoogleCalendarStatusResponse(IsConfigured, true, active.AccountEmail, active.CreatedAt, active.ConnectedByName,
                active.LastError, canManage);
    }

    public GoogleAuthorizationResponse StartAuthorization(string redirectUri)
    {
        EnsureConfigured();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = new AuthorizationState(currentUser.RequiredUserId, verifier, redirectUri, clock.GetUtcNow() + StateLifetime);
        var protectedState = protector.Protect(JsonSerializer.Serialize(state), StatePurpose);
        return new GoogleAuthorizationResponse(google.BuildAuthorizationUrl(redirectUri, protectedState, challenge));
    }

    public async Task<GoogleCalendarStatusResponse> CompleteAuthorizationAsync(string code, string state, CancellationToken ct)
    {
        EnsureConfigured();
        var parsed = ReadState(state);
        GoogleTokenGrant grant;
        try
        {
            grant = await google.ExchangeCodeAsync(code, parsed.RedirectUri, parsed.Verifier, ct);
        }
        catch (Exception ex) when (ex is GoogleCalendarException or HttpRequestException)
        {
            logger.LogWarning(ex, "Google sign-in could not be completed");
            throw new DomainException(ex is GoogleCalendarException ? ex.Message : "Google could not be reached. Please try again.");
        }
        var now = clock.GetUtcNow();

        var previous = await ActiveAsync(ct);
        previous?.Disconnect(now);
        if (previous is not null)
        {
            // The partial unique index allows one active row, so the old one is archived first.
            await db.SaveChangesAsync(ct);
        }

        var connection = new GoogleCalendarConnection(grant.AccountEmail,
            protector.Protect(grant.RefreshToken, RefreshTokenPurpose), currentUser.RequiredUserId);
        db.GoogleCalendarConnections.Add(connection);
        activity.Record(ActivityEntities.GoogleCalendar, connection.Id, ActivityActions.Connected, null,
            new { connection.AccountEmail });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Google Calendar account {Account} connected by {UserId}", connection.AccountEmail, currentUser.UserId);
        return await GetStatusAsync(ct);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        var connection = await ActiveAsync(ct) ?? throw new NotFoundException("Google Calendar connection", "active");
        try
        {
            await google.RevokeAsync(protector.Unprotect(connection.EncryptedRefreshToken, RefreshTokenPurpose), ct);
        }
        catch (Exception ex) when (ex is GoogleCalendarException or HttpRequestException or CryptographicException)
        {
            // Disconnecting still stops DOF using the account; the user can also remove access in their Google settings.
            logger.LogWarning(ex, "Google token for {Account} could not be revoked", connection.AccountEmail);
        }

        connection.Disconnect(clock.GetUtcNow());
        activity.Record(ActivityEntities.GoogleCalendar, connection.Id, ActivityActions.Disconnected,
            new { connection.AccountEmail }, null);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Google Calendar account {Account} disconnected by {UserId}", connection.AccountEmail, currentUser.UserId);
    }

    private AuthorizationState ReadState(string state)
    {
        AuthorizationState? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AuthorizationState>(protector.Unprotect(state, StatePurpose));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            throw new DomainException("The Google sign-in could not be verified. Please try connecting again.");
        }

        if (parsed is null || parsed.UserId != currentUser.UserId)
        {
            throw new DomainException("The Google sign-in was started by someone else. Please try connecting again.");
        }
        if (parsed.ExpiresAt < clock.GetUtcNow())
        {
            throw new DomainException("The Google sign-in took too long. Please try connecting again.");
        }
        return parsed;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new ConflictException(
                "Google Calendar is not set up on the server yet. Add the Google client settings (see DEPLOYMENT.md).");
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record AuthorizationState(Guid UserId, string Verifier, string RedirectUri, DateTimeOffset ExpiresAt);
}
