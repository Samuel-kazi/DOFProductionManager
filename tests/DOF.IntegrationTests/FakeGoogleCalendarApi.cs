using System.Collections.Concurrent;
using DOF.Application.Common.Abstractions;

namespace DOF.IntegrationTests;

/// <summary>
/// Stands in for Google in tests: holds events in memory, records every call, and can be told to fail
/// (Google down, or access revoked) or to report guests' answers.
/// </summary>
public sealed class FakeGoogleCalendarApi : IGoogleCalendarApi
{
    public const string GoodCode = "good-code";

    public sealed record Call(string Kind, string RefreshToken, string EventId, GoogleEventData? Data);

    public ConcurrentQueue<Call> Calls { get; } = new();
    public ConcurrentDictionary<string, GoogleEventData> Events { get; } = new();
    /// <summary>Answers Google reports, by e-mail (needsAction, accepted, tentative, declined).</summary>
    public ConcurrentDictionary<string, string> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The account the next successful sign-in connects.</summary>
    public string NextAccount { get; set; } = "dof.calendar@gmail.com";
    public string? LastRedirectUri { get; private set; }
    public string? LastVerifier { get; private set; }
    public Exception? FailWith { get; set; }

    public bool IsConfigured => true;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
        $"https://accounts.example.test/auth?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}&code_challenge={codeChallenge}";

    public Task<GoogleTokenGrant> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        if (code != GoodCode)
        {
            throw new GoogleCalendarException("Google sign-in failed (invalid_grant).");
        }
        LastRedirectUri = redirectUri;
        LastVerifier = codeVerifier;
        return Task.FromResult(new GoogleTokenGrant($"refresh-{NextAccount}", NextAccount));
    }

    public Task<IReadOnlyList<GoogleAttendeeResponse>> UpsertEventAsync(
        string refreshToken, string calendarId, GoogleEventData data, bool exists, CancellationToken ct)
    {
        ThrowIfFailing();
        Calls.Enqueue(new Call(exists && Events.ContainsKey(data.Id) ? "Update" : "Insert", refreshToken, data.Id, data));
        Events[data.Id] = data;
        return Task.FromResult(Responses(data));
    }

    public Task DeleteEventAsync(string refreshToken, string calendarId, string eventId, CancellationToken ct)
    {
        ThrowIfFailing();
        Calls.Enqueue(new Call("Delete", refreshToken, eventId, null));
        Events.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GoogleAttendeeResponse>?> GetResponsesAsync(
        string refreshToken, string calendarId, string eventId, CancellationToken ct)
    {
        ThrowIfFailing();
        Calls.Enqueue(new Call("Get", refreshToken, eventId, null));
        return Task.FromResult<IReadOnlyList<GoogleAttendeeResponse>?>(Events.TryGetValue(eventId, out var data) ? Responses(data) : null);
    }

    public Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        Calls.Enqueue(new Call("Revoke", refreshToken, string.Empty, null));
        return Task.CompletedTask;
    }

    public IReadOnlyList<Call> CallsFor(string eventId) => [.. Calls.Where(c => c.EventId == eventId)];

    private IReadOnlyList<GoogleAttendeeResponse> Responses(GoogleEventData data) =>
        [.. data.Attendees.Select(a => new GoogleAttendeeResponse(a.Email, Answers.GetValueOrDefault(a.Email, "needsAction")))];

    private void ThrowIfFailing()
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }
    }
}
