using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Shows;

namespace DOF.Desktop.ViewModels.Pages;

/// <summary>
/// The organisational calendar: month, week, day and agenda views of every item from
/// <c>GET /api/v1/calendar</c>. Items open their original record; rescheduling updates that record.
/// </summary>
public sealed partial class CalendarViewModel : PageViewModel
{
    public const int AgendaDays = 14;
    private readonly ApiClient _api;
    private readonly AuthSession _session;
    private readonly UserClock _clock;
    private readonly DesktopSettings _settings;
    private IReadOnlyList<CalendarItemViewModel> _loaded = [];
    private List<AssigneeOption>? _people;

    public CalendarViewModel(ApiClient api, AuthSession session, UserClock clock, PreferencesStore preferences, DesktopSettings settings)
    {
        _api = api;
        _session = session;
        _clock = clock;
        _settings = settings;
        Mode = Enum.TryParse<CalendarMode>(preferences.Current.DefaultCalendarView, out var preferred) ? preferred : CalendarMode.Month;
        AnchorDate = clock.Today;
        EventTypeFilters = [.. CalendarValues.EventTypes.Append(CalendarValues.MilestoneItem).Append(CalendarValues.TaskDeadlineItem)
            .Concat(CalendarValues.EpisodeItems).Append(CalendarValues.ReleaseItem)
            .Select(t => new FilterOption(t, CalendarLabels.Humanize(t)))];
        PriorityFilters = [.. CalendarValues.Priorities.Select(p => new FilterOption(p, p))];
        foreach (var option in EventTypeFilters.Concat(PriorityFilters))
        {
            option.PropertyChanged += (_, _) => _ = LoadAsync();
        }
    }

    public override string Title => "Calendar";

    public bool CanCreate => _session.CurrentUser?.Permissions.Contains("calendar.manage") == true;

    // ---- view state ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonth), nameof(IsWeek), nameof(IsDay), nameof(IsWeekOrDay), nameof(IsAgenda), nameof(RangeTitle))]
    public partial CalendarMode Mode { get; set; } = CalendarMode.Month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeTitle), nameof(PdfDayLabel), nameof(PdfWeekLabel), nameof(PdfMonthLabel), nameof(PdfYearLabel))]
    public partial DateOnly AnchorDate { get; set; }

    public bool IsMonth => Mode == CalendarMode.Month;
    public bool IsWeek => Mode == CalendarMode.Week;
    public bool IsDay => Mode == CalendarMode.Day;
    public bool IsWeekOrDay => Mode is CalendarMode.Week or CalendarMode.Day;
    public bool IsAgenda => Mode == CalendarMode.Agenda;

    public string RangeTitle => Mode switch
    {
        CalendarMode.Month => AnchorDate.ToString("MMMM yyyy"),
        CalendarMode.Day => AnchorDate.ToString("dddd d MMMM yyyy"),
        CalendarMode.Week => WeekTitle(),
        _ => $"{AnchorDate:d MMM} – {AnchorDate.AddDays(AgendaDays - 1):d MMM yyyy}",
    };

    public string TimeZoneText => $"Times in {_clock.Zone.Id}";

    public ObservableCollection<MonthDayCell> MonthCells { get; } = [];

    /// <summary>The month view by week row: day cells plus bars across days (multi-day events, session weeks).</summary>
    public ObservableCollection<MonthWeekRow> MonthWeeks { get; } = [];

    /// <summary>Where month bars start: below the day number.</summary>
    public const double MonthBarTop = 30;

    /// <summary>Bars of events lasting several days in the week view's all-day row.</summary>
    public ObservableCollection<CalendarBar> AllDayBars { get; } = [];
    public ObservableCollection<DayColumn> DayColumns { get; } = [];
    public ObservableCollection<AgendaGroup> AgendaGroups { get; } = [];
    public IReadOnlyList<string> WeekdayNames { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    public IReadOnlyList<string> Hours { get; } = [.. Enumerable.Range(0, 24).Select(h => $"{h:00}:00")];

    [ObservableProperty]
    public partial bool IsEmptyAgenda { get; private set; }

    // ---- filters ----

    public IReadOnlyList<FilterOption> EventTypeFilters { get; }
    public IReadOnlyList<FilterOption> PriorityFilters { get; }

    [ObservableProperty] public partial bool ShowFilters { get; set; }
    [ObservableProperty] public partial bool OnlyMine { get; set; }
    [ObservableProperty] public partial bool IncludeCancelled { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ActiveFilterCount { get; private set; }

    partial void OnOnlyMineChanged(bool value) => _ = LoadAsync();
    partial void OnIncludeCancelledChanged(bool value) => _ = LoadAsync();
    partial void OnSearchTextChanged(string value) => Rebuild();

    // ---- side panel: details or editor ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidePanel), nameof(ShowNewEventButton))]
    public partial object? SidePanel { get; private set; }

    public bool HasSidePanel => SidePanel is not null;

    /// <summary>Hidden while a panel is open, to keep the toolbar within the narrower calendar area.</summary>
    public bool ShowNewEventButton => CanCreate && !HasSidePanel;

    // ---- loading ----

    public (DateTimeOffset Start, DateTimeOffset End) VisibleRange()
    {
        var (first, days) = Mode switch
        {
            CalendarMode.Month => (MonthGridStart(), 42),
            CalendarMode.Week => (Monday(AnchorDate), 7),
            CalendarMode.Day => (AnchorDate, 1),
            _ => (AnchorDate, AgendaDays),
        };
        return (_clock.StartOfDay(first), _clock.StartOfDay(first.AddDays(days)));
    }

    public override async Task LoadAsync()
    {
        var (start, end) = VisibleRange();
        var query = $"api/v1/calendar?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(end.ToString("O"))}"
                    + FilterQuery();

        ActiveFilterCount = EventTypeFilters.Count(f => f.IsSelected) + PriorityFilters.Count(f => f.IsSelected)
                            + (OnlyMine ? 1 : 0) + (IncludeCancelled ? 1 : 0);

        await RunAsync(async () =>
        {
            var items = await _api.GetAsync<List<CalendarItemResponse>>(query);
            _loaded = [.. items.Select(i => new CalendarItemViewModel(i, _clock))];
            Rebuild();
        });
    }

    /// <summary>The active filters as query-string parameters (each starting with '&amp;').</summary>
    private string FilterQuery()
    {
        var query = string.Empty;
        foreach (var type in EventTypeFilters.Where(f => f.IsSelected))
        {
            query += $"&eventTypes={type.Value}";
        }
        foreach (var priority in PriorityFilters.Where(f => f.IsSelected))
        {
            query += $"&priorities={priority.Value}";
        }
        if (OnlyMine && _session.CurrentUser is { } me)
        {
            query += $"&assignedUserId={me.Id}";
        }
        if (IncludeCancelled)
        {
            query += "&includeCancelled=true";
        }
        return query;
    }

    // ---- schedule PDF ----

    public string PdfDayLabel => $"Day · {AnchorDate:ddd d MMM yyyy}";
    public string PdfWeekLabel => $"Week · {WeekTitle()}";
    public string PdfMonthLabel => $"Month · {AnchorDate:MMMM yyyy}";
    public string PdfYearLabel => $"Year · {AnchorDate.Year}";

    /// <summary>Where the save dialog opens (ExportFolder in appsettings.json, or Documents).</summary>
    public string ExportDirectory => _settings.ExportDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPdfMessage))]
    public partial string? PdfMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedPdf))]
    public partial string? SavedPdfPath { get; private set; }

    public bool HasPdfMessage => PdfMessage is not null;
    public bool HasSavedPdf => SavedPdfPath is not null;

    /// <summary>The export address for the period around the date in view, with the calendar's filters.</summary>
    public string PdfPath(string period) => $"api/v1/calendar/export?period={period}&date={AnchorDate:yyyy-MM-dd}" + FilterQuery();

    public string SuggestedPdfName(string period) => period switch
    {
        "Day" => $"DOF schedule - {AnchorDate:ddd d MMM yyyy}.pdf",
        "Week" => $"DOF schedule - week of {Monday(AnchorDate):d MMM yyyy}.pdf",
        "Month" => $"DOF schedule - {AnchorDate:MMMM yyyy}.pdf",
        _ => $"DOF schedule - {AnchorDate.Year}.pdf",
    };

    /// <summary>Downloads the PDF into <paramref name="destination"/>; <paramref name="savedPath"/> lets the user open it afterwards.</summary>
    public async Task DownloadPdfAsync(string period, Stream destination, string fileName, string? savedPath)
    {
        PdfMessage = null;
        SavedPdfPath = null;
        if (await RunAsync(async () =>
            {
                var bytes = await _api.GetBytesAsync(PdfPath(period));
                await destination.WriteAsync(bytes);
                await destination.FlushAsync();
            }))
        {
            PdfMessage = $"Saved \u201c{fileName}\u201d.";
            SavedPdfPath = savedPath;
            AppLog.Info($"Downloaded {period} schedule PDF");
        }
    }

    public void ReportPdfFailure(string message)
    {
        AppLog.Warning($"Schedule PDF could not be saved: {message}");
        SavedPdfPath = null;
        PdfMessage = $"The PDF could not be saved: {message}";
    }

    [RelayCommand]
    private void DismissPdfMessage()
    {
        PdfMessage = null;
        SavedPdfPath = null;
    }

    /// <summary>Rebuilds the visible view from the loaded items (and the local text search).</summary>
    private void Rebuild()
    {
        var term = SearchText.Trim();
        var items = term.Length == 0
            ? _loaded
            : [.. _loaded.Where(i => i.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                    || (i.Location?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false))];
        var today = _clock.Today;

        MonthCells.Clear();
        MonthWeeks.Clear();
        AllDayBars.Clear();
        DayColumns.Clear();
        AgendaGroups.Clear();

        switch (Mode)
        {
            case CalendarMode.Month:
                var first = MonthGridStart();
                for (var week = 0; week < 6; week++)
                {
                    var rowStart = first.AddDays(week * 7);
                    var inRow = items.Where(x => Enumerable.Range(0, 7).Any(d => x.OccursOn(rowStart.AddDays(d)))).ToList();
                    var (bars, covered) = CalendarBars.Build(inRow, rowStart, 7, joinSessionWeeks: true, onlyDayLong: false, top: MonthBarTop);
                    var space = CalendarBars.SpaceFor(bars);
                    var lanes = bars.Count == 0 ? 0 : bars.Max(b => b.Lane) + 1;
                    var cells = new List<MonthDayCell>();
                    for (var d = 0; d < 7; d++)
                    {
                        var day = rowStart.AddDays(d);
                        var cell = new MonthDayCell(day, day.Month == AnchorDate.Month, day == today,
                            [.. inRow.Where(x => !covered.Contains(x) && x.OccursOn(day)).OrderByDescending(x => x.IsDayLong).ThenBy(x => x.Start)],
                            space, lanes);
                        cells.Add(cell);
                        MonthCells.Add(cell);
                    }
                    MonthWeeks.Add(new MonthWeekRow(cells, bars));
                }
                break;

            case CalendarMode.Week or CalendarMode.Day:
                var columnStart = Mode == CalendarMode.Week ? Monday(AnchorDate) : AnchorDate;
                var columns = Mode == CalendarMode.Week ? 7 : 1;
                var visible = items.Where(x => Enumerable.Range(0, columns).Any(d => x.OccursOn(columnStart.AddDays(d)))).ToList();
                var (dayBars, inBars) = columns > 1
                    ? CalendarBars.Build(visible, columnStart, columns, joinSessionWeeks: false, onlyDayLong: true, top: 2)
                    : ([], new HashSet<CalendarItemViewModel>());
                foreach (var bar in dayBars)
                {
                    AllDayBars.Add(bar);
                }
                var barSpace = CalendarBars.SpaceFor(dayBars);
                for (var i = 0; i < columns; i++)
                {
                    var day = columnStart.AddDays(i);
                    var onDay = visible.Where(x => x.OccursOn(day)).ToList();
                    var timed = onDay.Where(x => !x.IsDayLong).Select(x => Segment(x, day)).ToList();
                    TimeSlotLayout.AssignLanes(timed);
                    DayColumns.Add(new DayColumn(day, day == today, [.. onDay.Where(x => x.IsDayLong && !inBars.Contains(x))], timed, barSpace));
                }
                break;

            default:
                for (var i = 0; i < AgendaDays; i++)
                {
                    var day = AnchorDate.AddDays(i);
                    var onDay = items.Where(x => x.OccursOn(day)).OrderByDescending(x => x.IsDayLong).ThenBy(x => x.Start).ToList();
                    if (onDay.Count > 0)
                    {
                        AgendaGroups.Add(new AgendaGroup(day, day == today, onDay));
                    }
                }
                IsEmptyAgenda = AgendaGroups.Count == 0;
                break;
        }
    }

    private static TimedItemViewModel Segment(CalendarItemViewModel item, DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var start = item.Start < dayStart ? dayStart : item.Start;
        var end = item.End > dayStart.AddDays(1) ? dayStart.AddDays(1) : item.End;
        return new TimedItemViewModel(item, (start - dayStart).TotalMinutes, Math.Max((end - dayStart).TotalMinutes, (start - dayStart).TotalMinutes + 15))
        {
            Day = day,
        };
    }

    // ---- navigation commands ----

    [RelayCommand]
    private Task SetModeAsync(CalendarMode mode)
    {
        Mode = mode;
        return LoadAsync();
    }

    [RelayCommand]
    private Task TodayAsync()
    {
        AnchorDate = _clock.Today;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PreviousAsync() => MoveAsync(-1);

    [RelayCommand]
    private Task NextAsync() => MoveAsync(1);

    private Task MoveAsync(int direction)
    {
        AnchorDate = Mode switch
        {
            CalendarMode.Month => AnchorDate.AddMonths(direction),
            CalendarMode.Week => AnchorDate.AddDays(7 * direction),
            CalendarMode.Day => AnchorDate.AddDays(direction),
            _ => AnchorDate.AddDays(AgendaDays * direction),
        };
        return LoadAsync();
    }

    [RelayCommand]
    private Task OpenDayAsync(DateOnly day)
    {
        AnchorDate = day;
        Mode = CalendarMode.Day;
        return LoadAsync();
    }

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var option in EventTypeFilters.Concat(PriorityFilters))
        {
            option.IsSelected = false;
        }
        OnlyMine = false;
        IncludeCancelled = false;
        SearchText = string.Empty;
    }

    // ---- records ----

    /// <summary>Opens the record behind an item. Standalone events open here; other sources open their own pages.</summary>
    [RelayCommand]
    private Task OpenItemAsync(CalendarItemViewModel item) =>
        item.Item.SourceType == "CalendarEvent"
            ? ShowEventAsync(item.Item.SourceId)
            : Navigator?.OpenRecordAsync(item.Item.SourceType, item.Item.SourceId) ?? Task.CompletedTask;

    /// <summary>Shows an event's details, moving the calendar to its date if it is not in view.</summary>
    public async Task OpenEventAsync(Guid eventId)
    {
        await RunAsync(async () =>
        {
            var calendarEvent = await _api.GetAsync<CalendarEventResponse>($"api/v1/calendar/events/{eventId}");
            var date = DateOnly.FromDateTime(_clock.ToLocal(calendarEvent.StartAt));
            var (start, end) = VisibleRange();
            if (calendarEvent.StartAt >= end || calendarEvent.EndAt <= start)
            {
                AnchorDate = date;
            }
            SidePanel = await DetailsAsync(calendarEvent);
        });
        await LoadAsync();
    }

    private async Task ShowEventAsync(Guid eventId) => await RunAsync(async () =>
        SidePanel = await DetailsAsync(await _api.GetAsync<CalendarEventResponse>($"api/v1/calendar/events/{eventId}")));

    /// <summary>An event's details with its Google Calendar invites section.</summary>
    private async Task<EventDetailsViewModel> DetailsAsync(CalendarEventResponse calendarEvent)
    {
        var invites = new GoogleInvitesViewModel(_api, _clock, calendarEvent, CanCreate);
        await invites.LoadAsync();
        return new EventDetailsViewModel(calendarEvent, _clock, invites);
    }

    [RelayCommand]
    private void CloseSidePanel() => SidePanel = null;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task NewEventAsync(DateOnly? date)
    {
        var people = await LoadPeopleAsync();
        if (people is not null)
        {
            SidePanel = EventEditorViewModel.ForNew(_clock, people, date ?? AnchorDate, _session.CurrentUser?.Id);
        }
    }

    /// <summary>Opens the week planner for the week in view, starting from the most recent series name.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task PlanWeekAsync()
    {
        await RunAsync(async () =>
        {
            var earlier = await _api.GetAsync<List<SessionWeekSummaryResponse>>("api/v1/calendar/session-weeks");
            SidePanel = new SessionWeekEditorViewModel(_clock, AnchorDate, KnownNames(earlier));
        });
    }

    /// <summary>Opens the whole week an event belongs to.</summary>
    [RelayCommand]
    private async Task EditWeekAsync(EventDetailsViewModel details)
    {
        if (details.Event.SessionWeekId is not { } weekId)
        {
            return;
        }
        await RunAsync(async () =>
        {
            var week = await _api.GetAsync<SessionWeekResponse>($"api/v1/calendar/session-weeks/{weekId}");
            var earlier = await _api.GetAsync<List<SessionWeekSummaryResponse>>("api/v1/calendar/session-weeks");
            SidePanel = new SessionWeekEditorViewModel(_clock, AnchorDate, KnownNames(earlier), week);
        });
    }

    /// <summary>Copies the time, place, speakers and days of the latest earlier week of the same series (not the themes).</summary>
    [RelayCommand]
    private async Task CopyLastWeekAsync(SessionWeekEditorViewModel editor)
    {
        editor.ErrorMessage = null;
        editor.Message = null;
        try
        {
            var earlier = await _api.GetAsync<List<SessionWeekSummaryResponse>>(
                $"api/v1/calendar/session-weeks?name={Uri.EscapeDataString(editor.Name.Trim())}&before={editor.WeekMonday:yyyy-MM-dd}");
            if (earlier.FirstOrDefault() is not { } last)
            {
                editor.Message = string.IsNullOrWhiteSpace(editor.Name)
                    ? "Type the sessions' name first, then copy its last week."
                    : $"No earlier week of {editor.Name.Trim()} to copy.";
                return;
            }
            editor.Apply(await _api.GetAsync<SessionWeekResponse>($"api/v1/calendar/session-weeks/{last.Id}"), copyThemes: false);
            editor.Message = $"Copied from the week of {last.WeekStart:d MMM yyyy}. Type this week's themes.";
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            editor.ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveWeekAsync(SessionWeekEditorViewModel editor)
    {
        if (editor.BuildRequest() is not { } request)
        {
            return;
        }
        editor.IsSaving = true;
        try
        {
            var saved = editor.Existing is { } existing
                ? await _api.PutAsync<SessionWeekResponse>($"api/v1/calendar/session-weeks/{existing.Id}", request)
                : await _api.PostAsync<SessionWeekResponse>("api/v1/calendar/session-weeks", request);
            AppLog.Info($"Saved session week {saved.Id} with {saved.Days.Count} days");
            SidePanel = null;
            AnchorDate = saved.WeekStart;
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            editor.ErrorMessage = ex.Message;
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    private static List<string> KnownNames(IEnumerable<SessionWeekSummaryResponse> weeks) =>
        [.. weeks.Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase)];

    [RelayCommand]
    private async Task EditEventAsync(EventDetailsViewModel details)
    {
        var people = await LoadPeopleAsync();
        if (people is not null)
        {
            SidePanel = EventEditorViewModel.ForExisting(_clock, people, details.Event);
        }
    }

    [RelayCommand]
    private async Task SaveEventAsync(EventEditorViewModel editor)
    {
        editor.ErrorMessage = null;
        if (editor.ResolveTimes() is not { } times)
        {
            return;
        }
        var repeat = editor.BuildRepeat(out var repeatValid);
        if (!repeatValid)
        {
            return;
        }

        editor.IsSaving = true;
        try
        {
            CalendarEventResponse saved;
            if (editor.Existing is { } existing)
            {
                saved = await _api.PutAsync<CalendarEventResponse>($"api/v1/calendar/events/{existing.Id}",
                    new UpdateCalendarEventRequest(editor.Title, editor.Description, editor.EventType, times.Start, times.End,
                        editor.IsAllDay, editor.Location, editor.MeetingUrl, editor.Priority, editor.Status,
                        editor.Assignments(), existing.Version, editor.IsInSeries && editor.ApplyToFollowing));
            }
            else
            {
                saved = await _api.PostAsync<CalendarEventResponse>("api/v1/calendar/events",
                    new CreateCalendarEventRequest(editor.Title, editor.Description, editor.EventType, times.Start, times.End,
                        editor.IsAllDay, editor.Location, editor.MeetingUrl, editor.Priority, editor.Assignments(), repeat));
            }

            SidePanel = await DetailsAsync(saved);
            AppLog.Info($"Saved calendar event {saved.Id}");
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
        catch (ApiUnavailableException ex)
        {
            editor.ErrorMessage = ex.Message;
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    [RelayCommand]
    private static void AskCancelEvent(EventDetailsViewModel details) => details.IsConfirmingCancel = true;

    [RelayCommand]
    private static void KeepEvent(EventDetailsViewModel details) => details.IsConfirmingCancel = false;

    [RelayCommand]
    private Task ConfirmCancelEventAsync(EventDetailsViewModel details) => CancelEventAsync(details, following: false);

    /// <summary>Cancels this event and every later one of its repeating series.</summary>
    [RelayCommand]
    private Task ConfirmCancelFollowingAsync(EventDetailsViewModel details) => CancelEventAsync(details, following: true);

    private async Task CancelEventAsync(EventDetailsViewModel details, bool following)
    {
        if (await RunAsync(() => _api.DeleteAsync($"api/v1/calendar/events/{details.Event.Id}{(following ? "?following=true" : "")}")))
        {
            AppLog.Info($"Cancelled calendar event {details.Event.Id}");
            SidePanel = null;
            await LoadAsync();
        }
    }

    /// <summary>
    /// Moves an event to a new local start, keeping its length. This updates the event record itself
    /// (with its current version), never a copy.
    /// </summary>
    public async Task<bool> RescheduleAsync(CalendarItemViewModel item, DateTime newLocalStart)
    {
        if (!item.CanReschedule)
        {
            return false;
        }

        if (item.Item.SourceType == "Milestone")
        {
            var movedMilestone = await RunAsync(async () =>
            {
                var m = await _api.GetAsync<MilestoneResponse>($"api/v1/milestones/{item.Item.SourceId}");
                await _api.PutAsync<MilestoneResponse>($"api/v1/milestones/{m.Id}", new UpdateMilestoneRequest(
                    m.Title, m.Description, DateOnly.FromDateTime(newLocalStart), m.Priority, m.Status, m.AssignedTo?.Id, m.Version));
                AppLog.Info($"Rescheduled milestone {m.Id}");
            });
            await LoadAsync();
            return movedMilestone;
        }

        if (item.Item.SourceType == "Task")
        {
            var movedTask = await RunAsync(async () =>
            {
                var t = await _api.GetAsync<TaskResponse>($"api/v1/tasks/{item.Item.SourceId}");
                await _api.PutAsync<TaskResponse>($"api/v1/tasks/{t.Id}", new UpdateTaskRequest(
                    t.Title, t.Description, t.Project?.Id, _clock.FromLocal(newLocalStart), t.Priority,
                    [.. t.Assignees.Select(a => a.UserId)], t.Assignees.FirstOrDefault(a => a.IsPrimary)?.UserId, t.Version,
                    t.Episode?.Id));
                AppLog.Info($"Rescheduled task {t.Id}");
            });
            await LoadAsync();
            return movedTask;
        }

        if (item.Item.SourceType == "Release")
        {
            var movedRelease = await RunAsync(async () =>
            {
                var r = await _api.GetAsync<ReleaseResponse>($"api/v1/releases/{item.Item.SourceId}");
                await _api.PatchAsync<ReleaseResponse>($"api/v1/releases/{r.Id}/schedule",
                    new RescheduleReleaseRequest(_clock.FromLocal(newLocalStart), r.Version));
                AppLog.Info($"Rescheduled release {r.Id}");
            });
            await LoadAsync();
            return movedRelease;
        }

        if (item.Item.SourceType == "Episode")
        {
            var movedEpisode = await RunAsync(async () =>
            {
                var e = await _api.GetAsync<EpisodeResponse>($"api/v1/episodes/{item.Item.SourceId}");
                await _api.PutAsync<EpisodeResponse>($"api/v1/episodes/{e.Id}", EpisodeMove.Apply(e, item.Item.EventType,
                    newLocalStart, _clock));
                AppLog.Info($"Rescheduled {item.Item.EventType} of episode {e.Id}");
            });
            await LoadAsync();
            return movedEpisode;
        }

        var moved = await RunAsync(async () =>
        {
            var current = await _api.GetAsync<CalendarEventResponse>($"api/v1/calendar/events/{item.Item.SourceId}");
            var duration = current.EndAt - current.StartAt;
            var start = _clock.FromLocal(newLocalStart);
            var saved = await _api.PutAsync<CalendarEventResponse>($"api/v1/calendar/events/{current.Id}",
                new UpdateCalendarEventRequest(current.Title, current.Description, current.EventType, start, start + duration,
                    current.IsAllDay, current.Location, current.MeetingUrl, current.Priority, current.Status,
                    [.. current.Assignments.Select(a => new CalendarAssignmentRequest(a.UserId, a.AssignmentType ?? "Attendee"))],
                    current.Version));
            if (SidePanel is EventDetailsViewModel open && open.Event.Id == saved.Id)
            {
                SidePanel = await DetailsAsync(saved);
            }
            AppLog.Info($"Rescheduled calendar event {saved.Id}");
        });
        await LoadAsync();
        return moved;
    }

    private async Task<List<AssigneeOption>?> LoadPeopleAsync()
    {
        if (_people is null)
        {
            var ok = await RunAsync(async () =>
            {
                var page = await _api.GetAsync<PagedResponse<UserResponse>>("api/v1/users?pageSize=100&isActive=true");
                _people = [.. page.Items.Select(u => (u.Id, u.FullName))
                    .Select(p => new AssigneeOption(p.Id, p.FullName))];
            });
            if (!ok)
            {
                return null;
            }
        }

        // Fresh option objects each time so editors don't share selection state.
        return [.. _people!.Select(p => new AssigneeOption(p.UserId, p.FullName))];
    }

    private DateOnly MonthGridStart() => Monday(new DateOnly(AnchorDate.Year, AnchorDate.Month, 1));

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private string WeekTitle()
    {
        var monday = Monday(AnchorDate);
        var sunday = monday.AddDays(6);
        return monday.Month == sunday.Month
            ? $"{monday.Day} – {sunday:d MMMM yyyy}"
            : $"{monday:d MMM} – {sunday:d MMM yyyy}";
    }
}
