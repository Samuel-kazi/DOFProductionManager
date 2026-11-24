namespace DOF.Application.Common.Abstractions;

/// <summary>An event time: a date for all-day events (end exclusive), otherwise an instant shown in <see cref="TimeZone"/>.</summary>
public sealed record GoogleEventTime(DateOnly? Date, DateTimeOffset? DateTime, string TimeZone);

public sealed record GoogleAttendee(string Email, string? DisplayName);

/// <summary>A guest's answer as Google reports it: needsAction, accepted, tentative or declined.</summary>
public sealed record GoogleAttendeeResponse(string Email, string ResponseStatus);

/// <summary>
/// One occurrence of a repeating event changed to its own title, description or time (a session day's
/// theme and speaker). <see cref="OriginalStart"/> is when the repeat rule puts it, which identifies it.
/// </summary>
public sealed record GoogleEventInstance(
    DateTimeOffset OriginalStart,
    string Summary,
    string? Description,
    GoogleEventTime Start,
    GoogleEventTime End);

/// <summary>
/// What DOF sends to Google for one event. <see cref="Id"/> is chosen by DOF, so inserts are idempotent.
/// A session week is one repeating event: <see cref="Recurrence"/> holds its RRULE and
/// <see cref="Instances"/> each day's own title and time.
/// </summary>
public sealed record GoogleEventData(
    string Id,
    string Summary,
    string? Description,
    string? Location,
    GoogleEventTime Start,
    GoogleEventTime End,
    IReadOnlyList<GoogleAttendee> Attendees,
    IReadOnlyList<string>? Recurrence = null,
    IReadOnlyList<GoogleEventInstance>? Instances = null);

public sealed record GoogleTokenGrant(string RefreshToken, string AccountEmail);

/// <summary>A call to Google failed. <see cref="AccessRevoked"/> means the account must be connected again.</summary>
public sealed class GoogleCalendarException(string message, bool accessRevoked = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool AccessRevoked { get; } = accessRevoked;
}

/// <summary>
/// Google sign-in (OAuth 2.0 with PKCE) and the Calendar API calls DOF needs. Every change is sent with
/// <c>sendUpdates=all</c>, so Google e-mails the guests and updates their calendars.
/// </summary>
public interface IGoogleCalendarApi
{
    /// <summary>Whether the server has a Google client id, secret and token encryption key.</summary>
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge);

    Task<GoogleTokenGrant> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct);

    /// <summary>
    /// Creates the event, or updates it if it already exists, keeping guests' answers. For a repeating event
    /// it then gives each listed occurrence its own title and time (without e-mailing guests again). Returns the answers.
    /// </summary>
    Task<IReadOnlyList<GoogleAttendeeResponse>> UpsertEventAsync(
        string refreshToken, string calendarId, GoogleEventData data, bool exists, CancellationToken ct);

    /// <summary>Deletes the event (guests are told). An event that is already gone counts as deleted.</summary>
    Task DeleteEventAsync(string refreshToken, string calendarId, string eventId, CancellationToken ct);

    /// <summary>The guests' current answers, or null when the event no longer exists in Google.</summary>
    Task<IReadOnlyList<GoogleAttendeeResponse>?> GetResponsesAsync(
        string refreshToken, string calendarId, string eventId, CancellationToken ct);

    Task RevokeAsync(string refreshToken, CancellationToken ct);
}

/// <summary>Encrypts secrets (such as Google refresh tokens) before they are stored. <paramref name="purpose"/> separates uses.</summary>
public interface ISecretProtector
{
    bool IsConfigured { get; }

    string Protect(string plaintext, string purpose);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the value was not made by this key and purpose.</summary>
    string Unprotect(string protectedValue, string purpose);
}

/// <summary>Draws a schedule PDF.</summary>
public interface ISchedulePdfRenderer
{
    byte[] Render(Calendar.ScheduleDocument document);
}
