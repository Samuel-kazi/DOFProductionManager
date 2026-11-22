namespace DOF.Contracts.Common;

/// <summary>Allowed values for project, milestone and task fields. Kept in step with the domain enums by a test.</summary>
public static class WorkValues
{
    public static readonly IReadOnlyList<string> ProjectStatuses =
        ["Proposed", "Approved", "Planning", "Active", "OnHold", "Completed", "Archived"];

    public static readonly IReadOnlyList<string> ProjectTypes =
        ["Documentary", "ShowProduction", "Conference", "Campaign", "MusicProduction", "Partnership", "Other"];

    /// <summary>Statuses for milestones and tasks.</summary>
    public static readonly IReadOnlyList<string> WorkStatuses =
        ["NotStarted", "InProgress", "AwaitingReview", "Completed", "Blocked", "Cancelled"];

    public const string Completed = "Completed";
    public const string Archived = "Archived";
}
