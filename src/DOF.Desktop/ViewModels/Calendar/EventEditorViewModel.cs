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

/// <summary>A choice in the editor's Repeat list.</summary>
public sealed record RepeatChoice(string Label, string? Frequency, int Interval = 1, bool PicksDays = false, bool Weekdays = false);

/// <summary>A day toggle for weekly repeats.</summary>
public sealed partial class WeekdayOption(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label => System.Globalization.CultureInfo.GetCultureInfo("en-GB").DateTimeFormat.GetAbbreviatedDayName(Day);

    [ObservableProperty] public partial bool IsSelected { get; set; }
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

    // ---- repeating ----

    public static readonly IReadOnlyList<RepeatChoice> RepeatChoices =
    [
        new("Does not repeat", null),
        new("Every day", "Daily"),
        new("Every weekday (Mon–Fri)", "Weekly", Weekdays: true),
        new("Every week", "Weekly", PicksDays: true),
        new("Every 2 weeks", "Weekly", 2, PicksDays: true),
        new("Every month", "Monthly"),
    ];

    public IReadOnlyList<RepeatChoice> RepeatOptions => RepeatChoices;

    public IReadOnlyList<WeekdayOption> WeekdayOptions { get; } =
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new WeekdayOption(d))];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Repeats), nameof(ShowRepeatDays))]
    public partial RepeatChoice SelectedRepeat { get; set; } = RepeatChoices[0];

    [ObservableProperty] public partial DateTime? RepeatUntil { get; set; }

    /// <summary>For an event of a series: apply the edit to this and the later events too.</summary>
    [ObservableProperty] public partial bool ApplyToFollowing { get; set; }

    public bool Repeats => IsNew && SelectedRepeat.Frequency is not null;
    public bool ShowRepeatDays => Repeats && SelectedRepeat.PicksDays;
    public bool IsInSeries => Existing?.SeriesId is not null;
    public string? RepeatText => Existing?.RepeatText;

    partial void OnSelectedRepeatChanged(RepeatChoice value)
    {
        if (value.PicksDays && WeekdayOptions.All(o => !o.IsSelected) && StartDate is { } start)
        {
            WeekdayOptions.Single(o => o.Day == start.DayOfWeek).IsSelected = true;
        }
        RepeatUntil ??= (StartDate ?? DateTime.Today).AddMonths(3);
    }

    /// <summary>The repeat rule for a new event, or null (with <see cref="ErrorMessage"/> set when it is incomplete).</summary>
    public RepeatRequest? BuildRepeat(out bool valid)
    {
        valid = true;
        if (!Repeats)
        {
            return null;
        }
        if (RepeatUntil is not { } until || StartDate is not { } start || until.Date <= start.Date)
        {
            ErrorMessage = "Choose when the repeating ends (after the first event).";
            valid = false;
            return null;
        }
        IReadOnlyList<string>? days = SelectedRepeat.Weekdays
            ? ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
            : SelectedRepeat.PicksDays ? [.. WeekdayOptions.Where(o => o.IsSelected).Select(o => o.Day.ToString())] : null;
        if (SelectedRepeat.PicksDays && days!.Count == 0)
        {
            ErrorMessage = "Tick the days the event repeats on.";
            valid = false;
            return null;
        }
        return new RepeatRequest(SelectedRepeat.Frequency!, SelectedRepeat.Interval, days, DateOnly.FromDateTime(until));
    }
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
