namespace DOF.Contracts.Responses;

public sealed record UserSummaryResponse(Guid Id, string FullName);

public sealed record ProjectRefResponse(Guid Id, string Name);

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Name,
    string ProjectType,
    string Status,
    string Priority,
    UserSummaryResponse? Manager,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    DateTimeOffset? CompletedAt,
    int MemberCount,
    int OpenTasks,
    int OverdueItems,
    string? NextMilestoneTitle,
    DateOnly? NextMilestoneDue);

public sealed record ProjectMemberResponse(Guid UserId, string FullName, string ProjectRole, DateTimeOffset JoinedAt);

public sealed record MilestoneResponse(
    Guid Id,
    ProjectRefResponse Project,
    string Title,
    string? Description,
    DateOnly DueDate,
    string Status,
    string Priority,
    UserSummaryResponse? AssignedTo,
    DateTimeOffset? CompletedAt,
    bool IsOverdue,
    int Version,
    bool CanEdit);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string ProjectType,
    string Status,
    string Priority,
    UserSummaryResponse? Manager,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<ProjectMemberResponse> Members,
    IReadOnlyList<MilestoneResponse> Milestones,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    bool CanArchive);

public sealed record TaskAssigneeResponse(Guid UserId, string FullName, bool IsPrimary);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    ProjectRefResponse? Project,
    DateTimeOffset? DueAt,
    string Priority,
    string Status,
    IReadOnlyList<TaskAssigneeResponse> Assignees,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset? CompletedAt,
    UserSummaryResponse? CompletedBy,
    bool IsOverdue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    EpisodeRefResponse? Episode = null);

/// <summary>One entry of a record's history. Values are shown as text; the change itself is in <see cref="Summary"/>.</summary>
public sealed record ActivityEntryResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    string Summary,
    UserSummaryResponse? User,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string?> OldValues,
    IReadOnlyDictionary<string, string?> NewValues);

/// <summary>A project's work in date order plus its full change history (project, milestones and tasks).</summary>
public sealed record ProjectTimelineResponse(
    ProjectRefResponse Project,
    IReadOnlyList<MilestoneResponse> Milestones,
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<ActivityEntryResponse> History);

/// <summary>Overdue work: tasks, milestones, and episodes with a missed production deadline or unrecorded past session.</summary>
public sealed record OverdueResponse(
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<MilestoneResponse> Milestones,
    IReadOnlyList<EpisodeSummaryResponse> Episodes);

public sealed record WorkloadSummaryResponse(
    Guid UserId,
    string FullName,
    int OpenTasks,
    int OverdueTasks,
    int DueThisWeek,
    int OpenMilestones);

public sealed record WorkloadResponse(
    WorkloadSummaryResponse Summary,
    IReadOnlyList<TaskResponse> OpenTasks,
    IReadOnlyList<MilestoneResponse> OpenMilestones);
