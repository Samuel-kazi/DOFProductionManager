namespace DOF.Contracts.Requests;

public sealed record CalendarAssignmentRequest(Guid UserId, string AssignmentType);

/// <summary>
/// For all-day events, StartAt is any moment on the first day and EndAt any moment on the last day
/// (or midnight after it); the server snaps them to whole days in the organisation's timezone.
/// </summary>
public sealed record CreateCalendarEventRequest(
    string Title,
    string? Description,
    string EventType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay,
    string? Location,
    string? MeetingUrl,
    string Priority,
    IReadOnlyList<CalendarAssignmentRequest>? Assignments);

public sealed record UpdateCalendarEventRequest(
    string Title,
    string? Description,
    string EventType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay,
    string? Location,
    string? MeetingUrl,
    string Priority,
    string Status,
    IReadOnlyList<CalendarAssignmentRequest>? Assignments,
    int Version);
