namespace DOF.Contracts.Common;

/// <summary>Allowed values for show, season and episode fields. Kept in step with the domain enums by a test.</summary>
public static class ShowValues
{
    public static readonly IReadOnlyList<string> ShowTypes = ["Recurring", "LimitedSeries", "OneOff", "SpecialProduction"];

    public static readonly IReadOnlyList<string> ShowStatuses = ["InDevelopment", "Active", "OnHiatus", "Ended", "Archived"];

    public static readonly IReadOnlyList<string> SeasonStatuses = ["Planned", "InProduction", "Completed", "Cancelled"];

    public static readonly IReadOnlyList<string> EpisodeStatuses =
    [
        "Planned", "Scheduled", "Recorded", "Editing", "InReview", "AwaitingApproval", "Approved", "ReadyForRelease",
        "PartiallyPublished", "Published", "Cancelled",
    ];

    /// <summary>Set from release confirmations (Phase 6), never by hand.</summary>
    public static readonly IReadOnlyList<string> PublicationStatuses = ["PartiallyPublished", "Published"];

    public static readonly IReadOnlyList<string> Responsibilities =
        ["Producer", "Director", "Presenter", "Editor", "Reviewer", "PublishingManager", "Crew"];

    /// <summary>Production timeline stage states.</summary>
    public const string StageDone = "Done";
    public const string StageCurrent = "Current";
    public const string StageUpcoming = "Upcoming";
    public const string StageCancelled = "Cancelled";

    public const string Archived = "Archived";
    public const string Cancelled = "Cancelled";
}
