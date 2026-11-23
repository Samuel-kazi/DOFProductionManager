using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;

namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>One day row of the week planner: include it or not, its theme, and who shares if not the week's speaker.</summary>
public sealed partial class SessionDayRow : ObservableObject
{
    public SessionDayRow(int offset, bool included)
    {
        Offset = offset;
        IsIncluded = included;
    }

    /// <summary>0 = Monday … 6 = Sunday.</summary>
    public int Offset { get; }

    public Guid? EventId { get; set; }

    [ObservableProperty] public partial DateOnly Date { get; set; }
    [ObservableProperty] public partial bool IsIncluded { get; set; }
    [ObservableProperty] public partial string? Theme { get; set; }
    [ObservableProperty] public partial string? Speaker { get; set; }

    /// <summary>Shown greyed in the day's speaker box: who shares when the box is left empty.</summary>
    [ObservableProperty] public partial string SpeakerHint { get; set; } = "Same as the week";

    public string DayText => Date.ToString("ddd d MMM", CultureInfo.GetCultureInfo("en-GB"));

    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(DayText));
}

/// <summary>
/// Plans a week of a session series (e.g. Morning Devotion): shared name, time, place and speaker, then each
/// day's theme and, when someone else shares that day, their name. Saving creates or updates one calendar
/// event per ticked day; unticking a saved day cancels it.
/// </summary>
public sealed partial class SessionWeekEditorViewModel : ObservableObject
{
    private readonly UserClock _clock;

    public SessionWeekEditorViewModel(UserClock clock, DateOnly anchor, IReadOnlyList<string> knownNames, SessionWeekResponse? existing = null)
    {
        _clock = clock;
        Existing = existing;
        KnownNames = knownNames;
        Days = [.. Enumerable.Range(0, 7).Select(i => new SessionDayRow(i, i < 5))];
        if (existing is null)
        {
            Name = knownNames.FirstOrDefault() ?? string.Empty;
            WeekStart = Monday(anchor).ToDateTime(TimeOnly.MinValue);
            StartTime = new TimeSpan(6, 0, 0);
            EndTime = new TimeSpan(7, 0, 0);
            EventType = "MinistryEvent";
        }
        else
        {
            Apply(existing, copyThemes: true);
        }
        UpdateDates();
        UpdateHints();
    }

    public SessionWeekResponse? Existing { get; }
    public bool IsNew => Existing is null;
    public string Heading => IsNew ? "Plan a week" : $"Edit week · {Existing!.Name}";
    public IReadOnlyList<string> KnownNames { get; }
    public IReadOnlyList<string> EventTypes => CalendarValues.EventTypes;
    public ObservableCollection<SessionDayRow> Days { get; }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? WeekStart { get; set; }
    [ObservableProperty] public partial TimeSpan? StartTime { get; set; }
    [ObservableProperty] public partial TimeSpan? EndTime { get; set; }
    [ObservableProperty] public partial string EventType { get; set; } = "MinistryEvent";
    [ObservableProperty] public partial string? Location { get; set; }
    [ObservableProperty] public partial string? MeetingUrl { get; set; }

    /// <summary>Who shares all week, unless a day names someone else.</summary>
    [ObservableProperty] public partial string? Speaker { get; set; }

    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;
    public bool HasMessage => Message is not null;

    public DateOnly WeekMonday => Monday(WeekStart is { } d ? DateOnly.FromDateTime(d) : _clock.Today);

    public string WeekText => $"Monday {WeekMonday.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"))} to " +
                              $"Sunday {WeekMonday.AddDays(6).ToString("d MMMM", CultureInfo.GetCultureInfo("en-GB"))}";

    partial void OnWeekStartChanged(DateTime? value)
    {
        // Any date picks its week: snap to the Monday.
        if (value is { } d && d.DayOfWeek != DayOfWeek.Monday)
        {
            WeekStart = Monday(DateOnly.FromDateTime(d)).ToDateTime(TimeOnly.MinValue);
            return;
        }
        UpdateDates();
        OnPropertyChanged(nameof(WeekText));
    }

    partial void OnSpeakerChanged(string? value) => UpdateHints();
    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>Fills the form from an earlier week: times, place, speakers and days. Themes are only kept when editing.</summary>
    public void Apply(SessionWeekResponse week, bool copyThemes)
    {
        Name = week.Name;
        EventType = week.EventType;
        Location = week.Location;
        MeetingUrl = week.MeetingUrl;
        Speaker = week.Speaker;
        if (copyThemes)
        {
            WeekStart = week.WeekStart.ToDateTime(TimeOnly.MinValue);
        }
        if (week.Days.FirstOrDefault() is { } first)
        {
            StartTime = _clock.ToLocal(first.StartAt).TimeOfDay;
            EndTime = _clock.ToLocal(first.EndAt).TimeOfDay;
        }
        foreach (var row in Days)
        {
            var day = week.Days.FirstOrDefault(d => DateOnly.FromDateTime(_clock.ToLocal(d.StartAt)) == week.WeekStart.AddDays(row.Offset));
            row.IsIncluded = day is not null;
            row.Speaker = day?.Speaker;
            row.Theme = copyThemes ? day?.Theme : null;
            row.EventId = copyThemes ? day?.EventId : null;
        }
        UpdateHints();
    }

    /// <summary>The request to save, or null with <see cref="ErrorMessage"/> set when something is missing.</summary>
    public SessionWeekRequest? BuildRequest()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Give the sessions a name, for example Morning Devotion.";
            return null;
        }
        if (StartTime is not { } start || EndTime is not { } end || end <= start)
        {
            ErrorMessage = "Choose a start and an end time (the end after the start).";
            return null;
        }
        var days = Days.Where(d => d.IsIncluded).Select(d => new SessionDayRequest(
            _clock.FromLocal(d.Date.ToDateTime(TimeOnly.MinValue) + start),
            _clock.FromLocal(d.Date.ToDateTime(TimeOnly.MinValue) + end),
            Clean(d.Theme), Clean(d.Speaker), d.EventId)).ToList();
        if (days.Count == 0)
        {
            ErrorMessage = "Tick at least one day.";
            return null;
        }
        return new SessionWeekRequest(Name.Trim(), WeekMonday, EventType, Clean(Location), Clean(MeetingUrl), Clean(Speaker), days, Existing?.Version);
    }

    private void UpdateDates()
    {
        foreach (var row in Days)
        {
            row.Date = WeekMonday.AddDays(row.Offset);
        }
    }

    private void UpdateHints()
    {
        foreach (var row in Days)
        {
            row.SpeakerHint = Clean(Speaker) is { } s ? s : "Who is sharing";
        }
    }

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
