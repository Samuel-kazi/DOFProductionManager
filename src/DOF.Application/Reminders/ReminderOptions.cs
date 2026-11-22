namespace DOF.Application.Reminders;

public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Delivery attempts before an e-mail is recorded as failed.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>How long a task may be overdue before production managers are told (escalation).</summary>
    public int EscalationAfterHours { get; set; } = 24;

    /// <summary>Local hour (organisation timezone) used as the "time" of date-only records such as milestones.</summary>
    public int DateOnlyReminderHour { get; set; } = 9;

    /// <summary>How far ahead the daily full reconciliation looks.</summary>
    public int ReconcileHorizonDays { get; set; } = 60;

    /// <summary>Processed outbox messages are kept this long.</summary>
    public int OutboxRetentionDays { get; set; } = 30;
}
