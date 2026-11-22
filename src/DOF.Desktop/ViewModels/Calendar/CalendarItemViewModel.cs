using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>A calendar item prepared for display in the user's timezone.</summary>
public sealed class CalendarItemViewModel
{
    public CalendarItemViewModel(CalendarItemResponse item, UserClock clock)
    {
        Item = item;
        Start = clock.ToLocal(item.StartAt);
        End = item.EndAt is { } end ? clock.ToLocal(end) : Start;
        var assigned = item.AssignedUsers;
        Initials = [.. assigned.Take(3).Select(a => Pages.TeamViewModel.Initials(a.FullName))];
        ExtraAssignees = Math.Max(0, assigned.Count - 3);
        AssigneeNames = assigned.Count == 0 ? "No one assigned" : string.Join(", ", assigned.Select(a => a.FullName));
    }

    public CalendarItemResponse Item { get; }
    public string Title => Item.Title;
    public DateTime Start { get; }
    public DateTime End { get; }
    public bool IsAllDay => Item.IsAllDay;
    public bool IsCancelled => Item.Status == Contracts.Common.CalendarValues.Cancelled;
    public string? Location => Item.Location;

    /// <summary>Resource key of the category colour (see Themes/Tokens.axaml).</summary>
    public string CategoryBrushKey => $"Brush.Cat.{Item.EventType}";

    public IReadOnlyList<string> Initials { get; }
    public int ExtraAssignees { get; }
    public string? ExtraAssigneesText => ExtraAssignees > 0 ? $"+{ExtraAssignees}" : null;
    public string AssigneeNames { get; }
    public bool HasNoOwner => Item.AssignedUsers.Count == 0;

    /// <summary>Only records the calendar itself owns can be dragged here; others reschedule in their own screens.</summary>
    public bool CanReschedule => Item.CanEdit && !IsCancelled && Item.SourceType == "CalendarEvent";

    /// <summary>Spans whole days (all-day, or 24 hours or more) and so belongs in the all-day row.</summary>
    public bool IsDayLong => IsAllDay || End - Start >= TimeSpan.FromHours(24);

    public string TimeText => IsAllDay
        ? (End.Date - Start.Date).TotalDays > 1 ? $"All day · until {End.AddDays(-1):ddd d MMM}" : "All day"
        : Start.Date == End.Date || End == Start.Date.AddDays(1)
            ? $"{Start:HH:mm}–{End:HH:mm}"
            : $"{Start:ddd d MMM HH:mm} – {End:ddd d MMM HH:mm}";

    public string WhenText => $"{Start:ddd d MMM} · {TimeText}";

    public string ToolTip => $"{Title}\n{WhenText}\n{AssigneeNames}" + (Location is null ? "" : $"\n{Location}");

    public bool OccursOn(DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        return Start < dayEnd && (End > dayStart || (End == Start && Start >= dayStart));
    }
}
