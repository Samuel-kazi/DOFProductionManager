namespace DOF.Contracts.Requests;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    string ProjectType,
    string Priority,
    Guid? ManagerId,
    DateOnly? StartDate,
    DateOnly? TargetEndDate);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string ProjectType,
    string Priority,
    Guid? ManagerId,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    int Version);

/// <summary>Version is optional for status changes; when given, a stale value is rejected.</summary>
public sealed record ChangeStatusRequest(string Status, int? Version = null);

public sealed record AddProjectMemberRequest(Guid UserId, string? ProjectRole);

public sealed record CreateMilestoneRequest(
    string Title,
    string? Description,
    DateOnly DueDate,
    string Priority,
    Guid? AssignedTo);

public sealed record UpdateMilestoneRequest(
    string Title,
    string? Description,
    DateOnly DueDate,
    string Priority,
    string Status,
    Guid? AssignedTo,
    int Version);

/// <summary>
/// If no assignees are given the creator becomes the primary responsible person. Assigning anyone else
/// requires the <c>tasks.assign</c> permission.
/// </summary>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    Guid? ProjectId,
    DateTimeOffset? DueAt,
    string Priority,
    IReadOnlyList<Guid>? AssigneeIds,
    Guid? PrimaryAssigneeId);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    Guid? ProjectId,
    DateTimeOffset? DueAt,
    string Priority,
    IReadOnlyList<Guid> AssigneeIds,
    Guid? PrimaryAssigneeId,
    int Version);

public sealed record AssignTaskRequest(Guid UserId, bool IsPrimary);
