namespace DOF.Contracts.Responses;

public sealed record AssignedUserResponse(Guid UserId, string FullName, string? AssignmentType);

/// <summary>
/// One entry on the aggregated calendar, built from its original record. <see cref="SourceType"/> and
/// <see cref="SourceId"/> identify that record; <see cref="Id"/> is unique per item (a record such as an
/// episode can contribute several items).
/// </summary>
public sealed record CalendarItemResponse(
    string Id,
    string Title,
    string SourceType,
    Guid SourceId,
    string EventType,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    bool IsAllDay,
    string Status,
    string Priority,
    string? Location,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool CanEdit,
    string? Speaker = null,
    Guid? SessionWeekId = null,
    string? SessionName = null,
    bool IsRepeating = false);

public sealed record CalendarRangeResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    string TimeZone,
    IReadOnlyList<CalendarItemResponse> Items);

public sealed record CalendarEventResponse(
    Guid Id,
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
    IReadOnlyList<AssignedUserResponse> Assignments,
    Guid? CreatedBy,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    string? Speaker = null,
    Guid? SessionWeekId = null,
    string? Theme = null,
    Guid? SeriesId = null,
    string? RepeatText = null);

/// <summary>
/// The signed-in user's day: today's calendar, upcoming items assigned to them, their open tasks,
/// tasks they created that are waiting for their review, and open episodes they produce or crew.
/// </summary>
public sealed record PersonalDashboardResponse(
    DateOnly Today,
    string TimeZone,
    IReadOnlyList<CalendarItemResponse> TodaySchedule,
    IReadOnlyList<CalendarItemResponse> Upcoming,
    IReadOnlyList<TaskResponse> MyTasks,
    IReadOnlyList<TaskResponse> PendingReviews,
    IReadOnlyList<EpisodeSummaryResponse> MyEpisodes);

public sealed record ManagementDashboardResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    string TimeZone,
    IReadOnlyList<CalendarItemResponse> UpcomingActivities,
    IReadOnlyList<CalendarItemResponse> Unassigned,
    IReadOnlyList<ProjectSummaryResponse> ActiveProjects,
    IReadOnlyList<MilestoneResponse> UpcomingMilestones,
    OverdueResponse Overdue,
    IReadOnlyList<WorkloadSummaryResponse> TeamWorkload,
    IReadOnlyList<EpisodeSummaryResponse> AwaitingPublication,
    IReadOnlyList<ReleaseResponse> UpcomingReleases);
