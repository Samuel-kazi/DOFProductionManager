using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>Read-only view of a standalone event (the original record behind its calendar item).</summary>
public sealed partial class EventDetailsViewModel(CalendarEventResponse calendarEvent, UserClock clock) : ObservableObject
{
    public CalendarEventResponse Event { get; } = calendarEvent;
    public string Title => Event.Title;
    public string TypeText => CalendarLabels.Humanize(Event.EventType);
    public string CategoryBrushKey => $"Brush.Cat.{Event.EventType}";
    public string StatusText => Event.Status;
    public string PriorityText => $"{Event.Priority} priority";
    public bool IsCancelled => Event.Status == Contracts.Common.CalendarValues.Cancelled;
    public string? Location => Event.Location;
    public string? MeetingUrl => Event.MeetingUrl;
    public string? Description => Event.Description;
    public bool CanEdit => Event.CanEdit;
    public bool CanCancel => Event.CanEdit && !IsCancelled;

    public string WhenText
    {
        get
        {
            var start = clock.ToLocal(Event.StartAt);
            var end = clock.ToLocal(Event.EndAt);
            if (Event.IsAllDay)
            {
                var lastDay = end.AddDays(-1);
                return lastDay.Date == start.Date ? $"{start:dddd d MMMM yyyy} · all day"
                    : $"{start:ddd d MMM} – {lastDay:ddd d MMM yyyy} · all day";
            }
            return start.Date == end.Date
                ? $"{start:dddd d MMMM yyyy} · {start:HH:mm}–{end:HH:mm}"
                : $"{start:ddd d MMM HH:mm} – {end:ddd d MMM yyyy HH:mm}";
        }
    }

    public IReadOnlyList<string> People =>
        Event.Assignments.Count == 0
            ? ["No one assigned yet"]
            : [.. Event.Assignments.Select(a => a.AssignmentType == "Organizer" ? $"{a.FullName} (organiser)" : a.FullName)];

    public string AuditText => Event.CreatedByName is null
        ? $"Created {clock.ToLocal(Event.CreatedAt):d MMM yyyy HH:mm}"
        : $"Created by {Event.CreatedByName} · {clock.ToLocal(Event.CreatedAt):d MMM yyyy HH:mm}";

    [ObservableProperty]
    public partial bool IsConfirmingCancel { get; set; }
}
