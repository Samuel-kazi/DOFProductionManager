using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>One person's notification choices: channels, overdue alerts and default reminder offsets.</summary>
public sealed class NotificationPreference : Entity
{
    public static readonly IReadOnlyList<int> DefaultOffsets = [60, 1440];

    private List<int> _offsets = [.. DefaultOffsets];

    private NotificationPreference() { }

    public NotificationPreference(Guid userId) => UserId = userId;

    public Guid UserId { get; private set; }
    public bool EmailEnabled { get; private set; }
    public bool DesktopEnabled { get; private set; } = true;
    public bool OverdueAlerts { get; private set; } = true;

    /// <summary>Minutes before a record's time at which automatic reminders fire.</summary>
    public IReadOnlyList<int> ReminderOffsets => _offsets;

    public void Update(bool email, bool desktop, bool overdueAlerts, IEnumerable<int> offsets)
    {
        var list = offsets.Distinct().Order().ToList();
        if (list.Count > 6 || list.Any(o => o < 0 || o > Reminder.MaxOffsetMinutes))
        {
            throw new DomainException("Choose up to six default reminders, each between 0 minutes and 60 days before.");
        }
        EmailEnabled = email;
        DesktopEnabled = desktop;
        OverdueAlerts = overdueAlerts;
        _offsets = list;
    }
}
