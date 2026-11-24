namespace DOF.Contracts.Common;

/// <summary>
/// Allowed values for calendar fields, shared by the API and the desktop client. Kept in step with the
/// domain enums by an automated test.
/// </summary>
public static class CalendarValues
{
    public static readonly IReadOnlyList<string> EventTypes =
        ["Meeting", "MinistryEvent", "Training", "ExternalAppointment", "RecordingSession", "Other"];

    public static readonly IReadOnlyList<string> Statuses = ["Scheduled", "Completed", "Cancelled"];

    public static readonly IReadOnlyList<string> RepeatFrequencies = ["Daily", "Weekly", "Monthly"];

    public static readonly IReadOnlyList<string> Priorities = ["Low", "Normal", "High", "Urgent"];

    public static readonly IReadOnlyList<string> AssignmentTypes = ["Organizer", "Attendee"];

    public static readonly IReadOnlyList<string> SourceTypes = ["CalendarEvent", "Milestone", "Task", "Episode", "Release"];

    /// <summary>Item event types contributed by sources other than standalone events.</summary>
    public const string MilestoneItem = "Milestone";
    public const string TaskDeadlineItem = "TaskDeadline";
    public const string RecordingItem = "Recording";
    public const string EditingDeadlineItem = "EditingDeadline";
    public const string ReviewDeadlineItem = "ReviewDeadline";
    public const string ApprovalDeadlineItem = "ApprovalDeadline";

    public const string ReleaseItem = "Release";

    /// <summary>The episode items: a timed recording session plus all-day production deadlines.</summary>
    public static readonly IReadOnlyList<string> EpisodeItems =
        [RecordingItem, EditingDeadlineItem, ReviewDeadlineItem, ApprovalDeadlineItem];

    public const string Cancelled = "Cancelled";
}
