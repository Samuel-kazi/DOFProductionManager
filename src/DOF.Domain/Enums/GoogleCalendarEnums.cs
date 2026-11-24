namespace DOF.Domain.Enums;

/// <summary>Where an event's Google Calendar invites stand.</summary>
public enum GoogleInviteStatus
{
    /// <summary>Waiting to be sent or updated in Google.</summary>
    Pending,
    /// <summary>Google has the current version of the event.</summary>
    Synced,
    /// <summary>Waiting to be taken off the guests' calendars.</summary>
    Removing,
    /// <summary>Taken off the guests' calendars (kept for history).</summary>
    Removed,
    /// <summary>Gave up after repeated errors; sending again retries.</summary>
    Failed,
}

/// <summary>A guest's answer, as reported by Google.</summary>
public enum GuestResponse
{
    Pending,
    Accepted,
    Tentative,
    Declined,
}

/// <summary>How often a repeating calendar event repeats.</summary>
public enum RepeatFrequency
{
    Daily,
    Weekly,
    Monthly,
}
