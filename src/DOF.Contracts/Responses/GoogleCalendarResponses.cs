namespace DOF.Contracts.Responses;

/// <summary>
/// The organisation's Google Calendar connection. <see cref="Configured"/> is false when the server has
/// no Google client settings, so nothing can be connected yet.
/// </summary>
public sealed record GoogleCalendarStatusResponse(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    DateTimeOffset? ConnectedAt,
    string? ConnectedByName,
    string? LastError,
    bool CanManage);

public sealed record GoogleAuthorizationResponse(string AuthorizationUrl);

/// <summary>One guest and their answer as Google last reported it (Pending, Accepted, Tentative, Declined).</summary>
public sealed record GoogleInviteGuestResponse(string Email, Guid? UserId, string? DisplayName, string Response);

/// <summary>
/// An event's Google Calendar invites. Status: Pending (being sent), Synced (guests have the latest
/// version), Removing, Removed, or Failed (see <see cref="LastError"/>).
/// </summary>
/// <summary>Invites for one event (<see cref="CalendarEventId"/>) or for a whole session week (<see cref="SessionWeekId"/>).</summary>
public sealed record GoogleInviteResponse(
    Guid? CalendarEventId,
    string Status,
    string? AccountEmail,
    IReadOnlyList<GoogleInviteGuestResponse> Guests,
    DateTimeOffset? LastSyncedAt,
    string? LastError,
    DateTimeOffset SentAt,
    string? SentByName,
    Guid? SessionWeekId = null);
