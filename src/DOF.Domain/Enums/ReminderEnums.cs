namespace DOF.Domain.Enums;

/// <summary>Records reminders can be attached to.</summary>
public enum ReminderSourceType
{
    Task,
    Milestone,
    CalendarEvent,
    Episode,
    Release,
}

/// <summary>
/// How a reminder is delivered. In-app reminders become notifications (which the desktop also shows as
/// pop-ups when the person allows it); email reminders go through the outbox.
/// </summary>
public enum ReminderChannel
{
    InApp,
    Email,
}

public enum ReminderStatus
{
    Pending,
    Processing,
    Delivered,
    Failed,
    Cancelled,
}

public enum NotificationType
{
    Reminder,
    Overdue,
    Escalation,
}

public enum OutboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
}
