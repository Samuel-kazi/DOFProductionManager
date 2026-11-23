namespace DOF.Contracts.Responses;

/// <summary>A day of a session week. <see cref="Speaker"/> is set only when it differs from the week's speaker.</summary>
public sealed record SessionDayResponse(
    Guid EventId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Theme,
    string? Speaker,
    string Title,
    string Status);

public sealed record SessionWeekResponse(
    Guid Id,
    string Name,
    DateOnly WeekStart,
    string EventType,
    string? Location,
    string? MeetingUrl,
    string? Speaker,
    IReadOnlyList<SessionDayResponse> Days,
    int Version,
    bool CanEdit);

/// <summary>A week in the list of earlier weeks, used to suggest names and to copy last week.</summary>
public sealed record SessionWeekSummaryResponse(Guid Id, string Name, DateOnly WeekStart, string? Speaker);
