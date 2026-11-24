using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DOF.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace DOF.Infrastructure.GoogleCalendar;

/// <summary>
/// Google OAuth 2.0 (authorization code with PKCE, loopback redirect) and Calendar API v3 over plain
/// HTTP. Access tokens are kept in memory until shortly before they expire; refresh tokens never leave
/// the server.
/// </summary>
public sealed class GoogleCalendarHttpApi(HttpClient http, IOptions<GoogleCalendarOptions> options, ISecretProtector protector)
    : IGoogleCalendarApi
{
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> AccessTokens = new();
    private static readonly IReadOnlyDictionary<string, string> NoAnswers = new Dictionary<string, string>();

    private GoogleCalendarOptions Settings => options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.ClientId) && !string.IsNullOrWhiteSpace(Settings.ClientSecret) && protector.IsConfigured;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = Settings.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = $"openid email {CalendarScope}",
            ["access_type"] = "offline",
            // Always ask, so Google returns a refresh token even when the account was connected before.
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return $"{Settings.AuthorizationEndpoint}?{string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}";
    }

    public async Task<GoogleTokenGrant> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var body = await TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        }, ct);

        var scopes = body["scope"]?.GetValue<string>().Split(' ') ?? [];
        if (!scopes.Contains(CalendarScope))
        {
            throw new GoogleCalendarException("Google did not grant access to calendar events. Connect again and allow DOF to manage your calendar events.");
        }
        var refreshToken = body["refresh_token"]?.GetValue<string>()
                           ?? throw new GoogleCalendarException("Google did not return offline access. Connect again.");
        var email = EmailFromIdToken(body["id_token"]?.GetValue<string>())
                    ?? throw new GoogleCalendarException("Google did not say which account was connected. Connect again.");

        if (body["access_token"]?.GetValue<string>() is { } access && body["expires_in"]?.GetValue<int>() is { } seconds)
        {
            AccessTokens[Key(refreshToken)] = (access, DateTimeOffset.UtcNow.AddSeconds(seconds - 60));
        }
        return new GoogleTokenGrant(refreshToken, email);
    }

    public async Task<IReadOnlyList<GoogleAttendeeResponse>> UpsertEventAsync(
        string refreshToken, string calendarId, GoogleEventData data, bool exists, CancellationToken ct)
    {
        var answers = await UpsertSeriesAsync(refreshToken, calendarId, data, exists, ct);
        foreach (var instance in data.Instances ?? [])
        {
            // Each day of a repeating event gets its own title, speaker and time. Guests were already told
            // about the series, so these per-day details are not e-mailed again.
            using var patch = await SendAsync(refreshToken, HttpMethod.Patch,
                $"{EventsPath(calendarId)}/{Uri.EscapeDataString(InstanceId(data.Id, instance.OriginalStart))}?sendUpdates=none",
                InstanceBody(instance), ct);
            if (patch.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
            {
                await EnsureSuccessAsync(patch, ct);
            }
        }
        return answers;
    }

    /// <summary>Google's id for one occurrence of a repeating timed event: "‹event id›_‹original start in UTC›".</summary>
    public static string InstanceId(string eventId, DateTimeOffset originalStart) =>
        $"{eventId}_{originalStart.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}";

    public static JsonObject InstanceBody(GoogleEventInstance instance) => new()
    {
        ["summary"] = instance.Summary,
        ["description"] = instance.Description,
        ["start"] = Time(instance.Start),
        ["end"] = Time(instance.End),
    };

    private async Task<IReadOnlyList<GoogleAttendeeResponse>> UpsertSeriesAsync(
        string refreshToken, string calendarId, GoogleEventData data, bool exists, CancellationToken ct)
    {
        if (!exists)
        {
            using var insert = await SendAsync(refreshToken, HttpMethod.Post, $"{EventsPath(calendarId)}?sendUpdates=all",
                EventBody(data, NoAnswers), ct);
            if (insert.StatusCode != HttpStatusCode.Conflict)
            {
                return Responses(await ReadAsync(insert, ct));
            }
            // 409: an earlier attempt already created it (the id is ours), so update it instead.
        }

        using var current = await SendAsync(refreshToken, HttpMethod.Get, $"{EventsPath(calendarId)}/{Uri.EscapeDataString(data.Id)}", null, ct);
        if (current.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            using var recreate = await SendAsync(refreshToken, HttpMethod.Post, $"{EventsPath(calendarId)}?sendUpdates=all",
                EventBody(data, NoAnswers), ct);
            return Responses(await ReadAsync(recreate, ct));
        }
        // Keep the answers guests have already given; PUT replaces the attendee list.
        var answers = Responses(await ReadAsync(current, ct)).ToDictionary(r => r.Email, r => r.ResponseStatus, StringComparer.OrdinalIgnoreCase);
        using var update = await SendAsync(refreshToken, HttpMethod.Put,
            $"{EventsPath(calendarId)}/{Uri.EscapeDataString(data.Id)}?sendUpdates=all", EventBody(data, answers), ct);
        return Responses(await ReadAsync(update, ct));
    }

    public async Task DeleteEventAsync(string refreshToken, string calendarId, string eventId, CancellationToken ct)
    {
        using var response = await SendAsync(refreshToken, HttpMethod.Delete,
            $"{EventsPath(calendarId)}/{Uri.EscapeDataString(eventId)}?sendUpdates=all", null, ct);
        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    public async Task<IReadOnlyList<GoogleAttendeeResponse>?> GetResponsesAsync(
        string refreshToken, string calendarId, string eventId, CancellationToken ct)
    {
        using var response = await SendAsync(refreshToken, HttpMethod.Get, $"{EventsPath(calendarId)}/{Uri.EscapeDataString(eventId)}", null, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }
        var body = await ReadAsync(response, ct);
        return body["status"]?.GetValue<string>() == "cancelled" ? null : Responses(body);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        AccessTokens.TryRemove(Key(refreshToken), out _);
        using var response = await http.PostAsync(Settings.RevocationEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken }), ct);
        // 400 means the token was already invalid, which is the outcome we want.
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    /// <summary>The JSON Google receives. Exposed for tests.</summary>
    public static JsonObject EventBody(GoogleEventData data, IReadOnlyDictionary<string, string> answers)
    {
        var body = SingleEventBody(data, answers);
        if (data.Recurrence is { Count: > 0 } rules)
        {
            body["recurrence"] = new JsonArray([.. rules.Select(r => (JsonNode)JsonValue.Create(r)!)]);
        }
        return body;
    }

    private static JsonObject SingleEventBody(GoogleEventData data, IReadOnlyDictionary<string, string> answers) => new()
    {
        ["id"] = data.Id,
        ["status"] = "confirmed",
        ["summary"] = data.Summary,
        ["description"] = data.Description,
        ["location"] = data.Location,
        ["start"] = Time(data.Start),
        ["end"] = Time(data.End),
        ["guestsCanModify"] = false,
        ["attendees"] = new JsonArray([.. data.Attendees.Select(a =>
        {
            var attendee = new JsonObject { ["email"] = a.Email };
            if (a.DisplayName is { } name)
            {
                attendee["displayName"] = name;
            }
            if (answers.TryGetValue(a.Email, out var answer))
            {
                attendee["responseStatus"] = answer;
            }
            return (JsonNode)attendee;
        })]),
    };

    private static JsonObject Time(GoogleEventTime time) => time.Date is { } date
        ? new JsonObject { ["date"] = date.ToString("yyyy-MM-dd") }
        : new JsonObject
        {
            ["dateTime"] = time.DateTime!.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["timeZone"] = time.TimeZone,
        };

    private static IReadOnlyList<GoogleAttendeeResponse> Responses(JsonObject body) =>
        body["attendees"] is JsonArray attendees
            ? [.. attendees.OfType<JsonObject>()
                .Where(a => a["organizer"]?.GetValue<bool>() != true && a["email"] is not null)
                .Select(a => new GoogleAttendeeResponse(a["email"]!.GetValue<string>(), a["responseStatus"]?.GetValue<string>() ?? "needsAction"))]
            : [];

    private string EventsPath(string calendarId) => $"{Settings.CalendarApiBase.TrimEnd('/')}/calendars/{Uri.EscapeDataString(calendarId)}/events";

    private async Task<HttpResponseMessage> SendAsync(string refreshToken, HttpMethod method, string url, JsonObject? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(refreshToken, ct));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            var response = await http.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 0)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    throw new GoogleCalendarException("Google rejected the connected account's access.", accessRevoked: true);
                }
                return response;
            }
            // The access token expired early or was revoked: get a fresh one and try once more.
            response.Dispose();
            AccessTokens.TryRemove(Key(refreshToken), out _);
        }
    }

    private async Task<string> AccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (AccessTokens.TryGetValue(Key(refreshToken), out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }
        var body = await TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);
        var token = body["access_token"]?.GetValue<string>() ?? throw new GoogleCalendarException("Google returned no access token.");
        var seconds = body["expires_in"]?.GetValue<int>() ?? 3600;
        AccessTokens[Key(refreshToken)] = (token, DateTimeOffset.UtcNow.AddSeconds(seconds - 60));
        return token;
    }

    private async Task<JsonObject> TokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Settings.ClientId;
        form["client_secret"] = Settings.ClientSecret;
        using var response = await http.PostAsync(Settings.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var body = Parse(text);
        if (!response.IsSuccessStatusCode)
        {
            var error = body?["error"]?.ToString();
            throw new GoogleCalendarException(
                error == "invalid_grant" ? "Google no longer accepts the connected account's access." : $"Google sign-in failed ({error ?? response.StatusCode.ToString()}).",
                accessRevoked: error == "invalid_grant");
        }
        return body ?? throw new GoogleCalendarException("Google sent an unreadable reply.");
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return Parse(await response.Content.ReadAsStringAsync(ct)) ?? throw new GoogleCalendarException("Google sent an unreadable reply.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var message = Parse(await response.Content.ReadAsStringAsync(ct))?["error"]?["message"]?.ToString();
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException($"Google Calendar is unavailable ({(int)response.StatusCode}).", null, response.StatusCode);
        }
        throw new GoogleCalendarException($"Google Calendar refused the change: {message ?? response.StatusCode.ToString()}");
    }

    private static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The account e-mail from the ID token Google returned directly over TLS (so its signature need not be checked).</summary>
    private static string? EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try
        {
            return (JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as JsonObject)?["email"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string Key(string refreshToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
