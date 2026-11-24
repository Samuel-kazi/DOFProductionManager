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
    IReadOnlyList<CalendarAssignmentRequest>? Assignments,
    RepeatRequest? Repeat = null);

/// <summary>
/// Makes a new event repeat: <see cref="Frequency"/> Daily, Weekly or Monthly, every <see cref="Interval"/>
/// days/weeks/months; for weekly, on <see cref="Weekdays"/> (day names, default the first event's day).
/// Ends on <see cref="Until"/> or after <see cref="Count"/> times (one of them).
/// </summary>
public sealed record RepeatRequest(
    string Frequency,
    int Interval = 1,
    IReadOnlyList<string>? Weekdays = null,
    DateOnly? Until = null,
    int? Count = null);

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
    int Version,
    bool ApplyToFollowing = false);
