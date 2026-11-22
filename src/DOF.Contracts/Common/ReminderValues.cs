namespace DOF.Contracts.Common;

/// <summary>Allowed values for reminders and notifications. Kept in step with the domain enums by a test.</summary>
public static class ReminderValues
{
    public static readonly IReadOnlyList<string> SourceTypes = ["Task", "Milestone", "CalendarEvent", "Episode", "Release"];
    public static readonly IReadOnlyList<string> Channels = ["InApp", "Email"];
    public static readonly IReadOnlyList<string> Statuses = ["Pending", "Processing", "Delivered", "Failed", "Cancelled"];
    public static readonly IReadOnlyList<string> NotificationTypes = ["Reminder", "Overdue", "Escalation"];

    /// <summary>Standard offsets in minutes before the record's time: at the time, 15 min, 1 hour, 1, 3 and 7 days.</summary>
    public static readonly IReadOnlyList<int> StandardOffsets = [0, 15, 60, 1440, 4320, 10080];

    public static string OffsetLabel(int minutes) => minutes switch
    {
        0 => "At the time",
        < 60 => $"{minutes} minutes before",
        60 => "1 hour before",
        < 1440 when minutes % 60 == 0 => $"{minutes / 60} hours before",
        1440 => "1 day before",
        _ when minutes % 1440 == 0 => $"{minutes / 1440} days before",
        _ => $"{minutes} minutes before",
    };
}
