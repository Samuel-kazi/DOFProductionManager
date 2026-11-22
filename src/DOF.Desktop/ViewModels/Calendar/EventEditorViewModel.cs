using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Calendar;

public sealed partial class AssigneeOption(Guid userId, string fullName) : ObservableObject
{
    public Guid UserId { get; } = userId;
    public string FullName { get; } = fullName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsOrganizer { get; set; }

    partial void OnIsOrganizerChanged(bool value)
    {
        if (value)
        {
            IsSelected = true;
        }
    }
}

/// <summary>Create or edit a standalone event. Times are entered in the user's timezone.</summary>
public sealed partial class EventEditorViewModel : ObservableObject
{
    private readonly UserClock _clock;

    private EventEditorViewModel(UserClock clock, IEnumerable<AssigneeOption> people, CalendarEventResponse? existing)
    {
        _clock = clock;
        Existing = existing;
        People = new ObservableCollection<AssigneeOption>(people);
    }

    public static EventEditorViewModel ForNew(UserClock clock, IEnumerable<AssigneeOption> people, DateOnly date, Guid? me)
    {
        var start = date == clock.Today
            ? clock.Now.Date.AddHours(clock.Now.Hour + 1) // next full hour today
            : date.ToDateTime(new TimeOnly(9, 0));
        var editor = new EventEditorViewModel(clock, people, null)
        {
            Title = string.Empty,
            EventType = "Meeting",
            Priority = "Normal",
            Status = "Scheduled",
        };
        editor.SetTimes(start, start.AddHours(1));
        if (me is { } id && editor.People.FirstOrDefault(p => p.UserId == id) is { } self)
        {
            self.IsOrganizer = true;
        }
        return editor;
    }

    public static EventEditorViewModel ForExisting(UserClock clock, IEnumerable<AssigneeOption> people, CalendarEventResponse existing)
    {
        var editor = new EventEditorViewModel(clock, people, existing)
        {
            Title = existing.Title,
            Description = existing.Description,
            EventType = existing.EventType,
            Priority = existing.Priority,
            Status = existing.Status,
            Location = existing.Location,
            MeetingUrl = existing.MeetingUrl,
            IsAllDay = existing.IsAllDay,
        };
        var start = clock.ToLocal(existing.StartAt);
        var end = clock.ToLocal(existing.EndAt);
        editor.SetTimes(start, existing.IsAllDay ? end.AddDays(-1) : end);
        foreach (var assignment in existing.Assignments)
        {
            if (editor.People.FirstOrDefault(p => p.UserId == assignment.UserId) is { } person)
            {
                person.IsSelected = true;
                person.IsOrganizer = assignment.AssignmentType == "Organizer";
            }
        }
        return editor;
    }

    public CalendarEventResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public string Heading => IsNew ? "New event" : "Edit event";

    public IReadOnlyList<string> EventTypes => CalendarValues.EventTypes;
    public IReadOnlyList<string> Priorities => CalendarValues.Priorities;
    public IReadOnlyList<string> Statuses => CalendarValues.Statuses;
    public ObservableCollection<AssigneeOption> People { get; }

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string EventType { get; set; } = "Meeting";
    [ObservableProperty] public partial string Priority { get; set; } = "Normal";
    [ObservableProperty] public partial string Status { get; set; } = "Scheduled";
    [ObservableProperty] public partial string? Location { get; set; }
    [ObservableProperty] public partial string? MeetingUrl { get; set; }
    [ObservableProperty] public partial bool IsAllDay { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial TimeSpan? StartTime { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial TimeSpan? EndTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    private void SetTimes(DateTime start, DateTime end)
    {
        StartDate = start.Date;
        StartTime = start.TimeOfDay;
        EndDate = end.Date;
        EndTime = end.TimeOfDay;
    }

    /// <summary>Keeps the event's length when the start moves, as calendar users expect.</summary>
    partial void OnStartDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (oldValue is { } before && newValue is { } after && EndDate is { } end)
        {
            EndDate = end + (after.Date - before.Date);
        }
    }

    /// <summary>Builds the request, or returns null with <see cref="ErrorMessage"/> set when input is incomplete.</summary>
    public (DateTimeOffset Start, DateTimeOffset End)? ResolveTimes()
    {
        if (StartDate is null || EndDate is null || (!IsAllDay && (StartTime is null || EndTime is null)))
        {
            ErrorMessage = "Choose a start and end.";
            return null;
        }

        var start = StartDate.Value.Date + (IsAllDay ? TimeSpan.Zero : StartTime!.Value);
        var end = IsAllDay ? EndDate.Value.Date.AddDays(1) : EndDate.Value.Date + EndTime!.Value;
        if (end <= start)
        {
            ErrorMessage = "The event must end after it starts.";
            return null;
        }

        return (_clock.FromLocal(start), _clock.FromLocal(end));
    }

    public IReadOnlyList<CalendarAssignmentRequest> Assignments() =>
        [.. People.Where(p => p.IsSelected).Select(p => new CalendarAssignmentRequest(p.UserId, p.IsOrganizer ? "Organizer" : "Attendee"))];
}
