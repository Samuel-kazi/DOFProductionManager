namespace DOF.Contracts.Common;

/// <summary>Allowed values for releases and channels. Kept in step with the domain enums by a test.</summary>
public static class ReleaseValues
{
    public static readonly IReadOnlyList<string> Statuses =
        ["Draft", "AwaitingApproval", "Ready", "Scheduled", "Published", "Postponed", "Overdue", "Cancelled", "Failed"];

    /// <summary>Statuses an ordinary edit may set. Publish, postpone and cancel have their own actions; overdue is derived.</summary>
    public static readonly IReadOnlyList<string> WorkflowStatuses = ["Draft", "AwaitingApproval", "Ready", "Scheduled", "Failed"];

    public static readonly IReadOnlyList<string> ChannelTypes = ["Video", "Audio", "Social", "Broadcast", "Web", "Other"];

    /// <summary>Release list views (spec 6.4), used by <c>GET /releases?view=</c>.</summary>
    public static readonly IReadOnlyList<string> Views = ["All", "Ready", "Scheduled", "Published", "Postponed", "Overdue", "Cancelled"];

    public const string ReleaseItem = "Release";
    public const string Published = "Published";
    public const string Overdue = "Overdue";
    public const string Cancelled = "Cancelled";
}
