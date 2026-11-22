using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Shows;

namespace DOF.Desktop.ViewModels.Reminders;

public sealed class ReminderRowViewModel(ReminderResponse reminder, UserClock clock)
{
    public ReminderResponse Reminder { get; } = reminder;
    public Guid Id => Reminder.Id;
    public string Title => Reminder.SourceTitle ?? "(record no longer available)";
    public string KindText => CalendarLabels.Humanize(Reminder.SourceType == "CalendarEvent" ? "Event" : Reminder.SourceType);
    public string WhenText => (Reminder.Status == "Delivered" && Reminder.DeliveredAt is { } d ? $"Delivered {ShowLabels.When(d, clock)}"
        : ShowLabels.When(Reminder.ScheduledAt, clock));
    public string RuleText => (Reminder.OffsetMinutes is { } o ? ReminderValues.OffsetLabel(o) : "At a set time")
                              + (Reminder.IsAutomatic ? " · default" : " · personal")
                              + (Reminder.Channel == "Email" ? " · e-mail" : "");
    public string StatusText => Reminder.Status;
    public string StatusBrushKey => Reminder.Status switch
    {
        "Delivered" => "Brush.Online",
        "Failed" => "Brush.Offline",
        "Cancelled" => "Brush.TextMuted",
        _ => "Brush.Accent",
    };
    public string? Detail => Reminder.Status switch
    {
        "Failed" => $"Failed after {Reminder.RetryCount} attempts: {Reminder.LastError}",
        "Processing" when Reminder.RetryCount > 0 => $"Retrying ({Reminder.RetryCount} failed): {Reminder.LastError}",
        "Cancelled" => Reminder.CancellationReason,
        _ => null,
    };
    public bool HasDetail => Detail is not null;
    public bool IsFailed => Reminder.Status == "Failed";
    public bool CanCancel => Reminder.Status == "Pending";
}

public sealed partial class OffsetOption(int minutes) : ObservableObject
{
    public int Minutes { get; } = minutes;
    public string Label { get; } = ReminderValues.OffsetLabel(minutes);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    public override string ToString() => Label;
}

/// <summary>"Remind me" on a record's details: choose how long before, then add.</summary>
public sealed partial class RemindMeViewModel(string sourceType, Guid sourceId, bool hasTime) : ObservableObject
{
    public string SourceType { get; } = sourceType;
    public Guid SourceId { get; } = sourceId;
    public bool IsAvailable { get; } = hasTime;
    public IReadOnlyList<OffsetOption> Options { get; } = [.. ReminderValues.StandardOffsets.Select(m => new OffsetOption(m))];

    [ObservableProperty] public partial OffsetOption? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;
}
