using CommunityToolkit.Mvvm.ComponentModel;

namespace DOF.Desktop.ViewModels.Calendar;

public enum CalendarMode
{
    Month,
    Week,
    Day,
    Agenda,
}

public sealed class MonthDayCell(DateOnly date, bool isCurrentMonth, bool isToday, IReadOnlyList<CalendarItemViewModel> items)
{
    public const int VisibleItems = 3;

    public DateOnly Date { get; } = date;
    public string DayNumber => Date.Day == 1 ? Date.ToString("d MMM") : Date.Day.ToString();
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<CalendarItemViewModel> Items { get; } = items.Take(VisibleItems).ToList();
    public int HiddenCount { get; } = Math.Max(0, items.Count - VisibleItems);
    public string? MoreText => HiddenCount > 0 ? $"+{HiddenCount} more" : null;
}

public sealed class DayColumn(DateOnly date, bool isToday, IReadOnlyList<CalendarItemViewModel> allDay, IReadOnlyList<TimedItemViewModel> timed)
{
    public DateOnly Date { get; } = date;
    public string Header => Date.ToString("ddd d");
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<CalendarItemViewModel> AllDayItems { get; } = allDay;
    public IReadOnlyList<TimedItemViewModel> TimedItems { get; } = timed;
}

public sealed class AgendaGroup(DateOnly date, bool isToday, IReadOnlyList<CalendarItemViewModel> items)
{
    public DateOnly Date { get; } = date;
    public string Header => IsToday ? $"Today · {Date:dddd d MMMM}" : Date.ToString("dddd d MMMM");
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<CalendarItemViewModel> Items { get; } = items;
}

public sealed partial class FilterOption(string value, string label) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public static class CalendarLabels
{
    public static string Humanize(string value) => value switch
    {
        "MinistryEvent" => "Ministry event",
        "ExternalAppointment" => "External appointment",
        "RecordingSession" => "Recording session",
        "CalendarEvent" => "Event",
        "TaskDeadline" => "Task deadline",
        "Recording" => "Episode recording",
        "EditingDeadline" => "Editing deadline",
        "ReviewDeadline" => "Review deadline",
        "ApprovalDeadline" => "Approval deadline",
        "InDevelopment" => "In development",
        "OnHiatus" => "On hiatus",
        "InProduction" => "In production",
        "InReview" => "In review",
        "AwaitingApproval" => "Awaiting approval",
        "ReadyForRelease" => "Ready for release",
        "PartiallyPublished" => "Partially published",
        "LimitedSeries" => "Limited series",
        "OneOff" => "One-off",
        "SpecialProduction" => "Special production",
        "PublishingManager" => "Publishing manager",
        "NotStarted" => "Not started",
        "InProgress" => "In progress",
        "AwaitingReview" => "Awaiting review",
        "OnHold" => "On hold",
        "ShowProduction" => "Show production",
        "MusicProduction" => "Music production",
        _ => value,
    };
}
