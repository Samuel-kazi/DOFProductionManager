namespace DOF.Contracts.Requests;

/// <summary>
/// One day of a session week. <see cref="Speaker"/> is only needed when someone other than the week's
/// speaker shares that day. <see cref="EventId"/> identifies an existing day when the week is edited.
/// </summary>
public sealed record SessionDayRequest(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Theme,
    string? Speaker = null,
    Guid? EventId = null);

/// <summary>
/// A week of a session series (Monday <see cref="WeekStart"/>): the shared name, type, place, link and
/// speaker, and the days it runs. Editing replaces the days: days left out are cancelled.
/// </summary>
public sealed record SessionWeekRequest(
    string Name,
    DateOnly WeekStart,
    string EventType,
    string? Location,
    string? MeetingUrl,
    string? Speaker,
    IReadOnlyList<SessionDayRequest> Days,
    int? Version = null);
