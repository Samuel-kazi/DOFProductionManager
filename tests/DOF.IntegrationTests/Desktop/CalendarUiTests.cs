using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Controls;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.Views;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests.Desktop;

/// <summary>
/// The desktop calendar against the real API: views, opening records, the editor, drag-to-reschedule
/// (with simulated mouse input), filters, dashboards and quick create.
/// </summary>
public class CalendarUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");

    // Each test uses its own far-future week (Mondays in 2032) so tests sharing the database don't collide.
    private static DateOnly Monday(int n) => new DateOnly(2032, 3, 1).AddDays(7 * n);

    /// <summary>A UTC instant for a Nairobi wall-clock time.</summary>
    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), Nairobi), TimeSpan.Zero);

    private async Task<CalendarEventResponse> CreateEventAsync(string title, DateTimeOffset start, DateTimeOffset end,
        string type = "Meeting", params CalendarAssignmentRequest[] people)
    {
        var admin = await factory.AdminClientAsync();
        return await (await admin.PostAsJsonAsync("/api/v1/calendar/events",
            new CreateCalendarEventRequest(title, null, type, start, end, false, "Studio A", null, "Normal", people), Ct))
            .DataAsync<CalendarEventResponse>();
    }

    private async Task<CalendarEventResponse> GetEventAsync(Guid id) =>
        await (await (await factory.AdminClientAsync()).GetAsync($"/api/v1/calendar/events/{id}", Ct))
            .DataAsync<CalendarEventResponse>();

    private static async Task<CalendarViewModel> OpenCalendarAsync(ShellViewModel shell, DateOnly anchor, CalendarMode mode)
    {
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        calendar.AnchorDate = anchor;
        await calendar.SetModeCommand.ExecuteAsync(mode);
        Assert.False(calendar.HasError, calendar.ErrorMessage);
        return calendar;
    }

    /// <summary>The on-screen block for an item, found by title.</summary>
    private static Border ItemBlock(MainWindow window, string title) =>
        Find<Border>(window).First(b => b.Classes.Contains("item") && b.DataContext switch
        {
            CalendarItemViewModel i => i.Title == title,
            TimedItemViewModel t => t.Item.Title == title,
            _ => false,
        } && b.IsEffectivelyVisible);

    private static Point CentreOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, Math.Min(10, control.Bounds.Height / 2)), window)!.Value;

    private static async Task DragAsync(MainWindow window, Point from, Vector by)
    {
        window.MouseMove(from, RawInputModifiers.None);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(from + by / 4, RawInputModifiers.LeftMouseButton);
        window.MouseMove(from + by, RawInputModifiers.LeftMouseButton);
        window.MouseUp(from + by, MouseButton.Left, RawInputModifiers.None);
        await Task.Delay(50);
        Render(window);
    }

    private static void Click(MainWindow window, Point at)
    {
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
    }

    [Fact]
    public Task Events_appear_on_the_right_days_and_times_in_every_view() => Ui.Run(headless, async () =>
    {
        var week = Monday(0);
        var wednesday = week.AddDays(2);
        await CreateEventAsync("Staff meeting", At(wednesday, 9), At(wednesday, 10));
        await CreateEventAsync("Choir rehearsal", At(wednesday, 9, 30), At(wednesday, 11), "MinistryEvent");
        await CreateEventAsync("Faith Frequency recording", At(week.AddDays(4), 14), At(week.AddDays(4), 17), "RecordingSession");
        await CreateEventAsync("Camera training", At(week.AddDays(1), 8), At(week.AddDays(1), 12), "Training");

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);

        // Month: items sit in their day's cell.
        var calendar = await OpenCalendarAsync(shell, wednesday, CalendarMode.Month);
        var wednesdayCell = calendar.MonthCells.Single(c => c.Date == wednesday);
        Assert.Equal(["Staff meeting", "Choir rehearsal"], wednesdayCell.Items.Select(i => i.Title));
        Assert.Equal(42, calendar.MonthCells.Count);
        Assert.Equal(DayOfWeek.Monday, calendar.MonthCells[0].Date.DayOfWeek);
        Snapshot(window, "10-calendar-month");

        // Week: Nairobi times become minutes from midnight; overlapping items share the column side by side.
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);
        Assert.Equal(7, calendar.DayColumns.Count);
        var wednesdayColumn = calendar.DayColumns.Single(c => c.Date == wednesday);
        var meeting = wednesdayColumn.TimedItems.Single(t => t.Item.Title == "Staff meeting");
        var choir = wednesdayColumn.TimedItems.Single(t => t.Item.Title == "Choir rehearsal");
        Assert.Equal(9 * 60, meeting.StartMinute);
        Assert.Equal(10 * 60, meeting.EndMinute);
        Assert.Equal((2, 2), (meeting.LaneCount, choir.LaneCount));
        Assert.NotEqual(meeting.Lane, choir.Lane);
        Snapshot(window, "11-calendar-week");

        // Day.
        await calendar.OpenDayCommand.ExecuteAsync(wednesday);
        Assert.True(calendar.IsDay);
        Assert.Equal(2, Assert.Single(calendar.DayColumns).TimedItems.Count);
        Snapshot(window, "12-calendar-day");

        // Agenda: grouped by day, in order.
        calendar.AnchorDate = week;
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Agenda);
        Assert.Equal([week.AddDays(1), wednesday, week.AddDays(4)],
            calendar.AgendaGroups.Select(g => g.Date).Where(d => d < week.AddDays(7)));
        Snapshot(window, "13-calendar-agenda");
        window.Close();
    });

    [Fact]
    public Task Clicking_an_item_opens_its_event_record() => Ui.Run(headless, async () =>
    {
        var day = Monday(1).AddDays(1);
        var created = await CreateEventAsync("Board meeting", At(day, 9), At(day, 10), "Meeting");

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), day, CalendarMode.Week);

        Click(window, CentreOf(ItemBlock(window, "Board meeting"), window));
        var details = await WaitForAsync(() => calendar.SidePanel as EventDetailsViewModel);

        Assert.Equal(created.Id, details.Event.Id);
        Assert.Contains("09:00–10:00", details.WhenText);
        Assert.True(details.CanEdit);
        Snapshot(window, "14-calendar-event-details");
        window.Close();
    });

    [Fact]
    public Task Events_created_in_the_editor_are_saved_with_their_organiser() => Ui.Run(headless, async () =>
    {
        var day = Monday(2).AddDays(3);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var calendar = await OpenCalendarAsync(shell, day, CalendarMode.Week);

        await calendar.NewEventCommand.ExecuteAsync(day);
        var editor = Assert.IsType<EventEditorViewModel>(calendar.SidePanel);
        Assert.Equal(day.ToDateTime(TimeOnly.MinValue), editor.StartDate);
        Assert.Contains(editor.People, p => p.IsOrganizer && p.FullName == "System Administrator");
        editor.Title = "Partners breakfast";
        editor.EventType = "ExternalAppointment";
        editor.StartTime = new TimeSpan(7, 30, 0);
        editor.EndTime = new TimeSpan(9, 0, 0);
        editor.Location = "Serena Hotel";
        Snapshot(window, "15-calendar-editor");

        await calendar.SaveEventCommand.ExecuteAsync(editor);

        var details = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);
        var saved = await GetEventAsync(details.Event.Id);
        Assert.Equal(At(day, 7, 30), saved.StartAt);
        Assert.Equal(At(day, 9), saved.EndAt);
        Assert.Equal("Organizer", Assert.Single(saved.Assignments).AssignmentType);
        Assert.Contains(calendar.DayColumns.Single(c => c.Date == day).TimedItems, t => t.Item.Title == "Partners breakfast");

        // Validation errors from the server are shown in the editor.
        await calendar.NewEventCommand.ExecuteAsync(day);
        var invalid = Assert.IsType<EventEditorViewModel>(calendar.SidePanel);
        invalid.Title = "Bad link";
        invalid.MeetingUrl = "not a link";
        await calendar.SaveEventCommand.ExecuteAsync(invalid);
        Assert.Contains("Meeting link", invalid.ErrorMessage);
        window.Close();
    });

    [Fact]
    public Task Dragging_in_the_week_view_moves_the_event_record() => Ui.Run(headless, async () =>
    {
        var tuesday = Monday(3).AddDays(1);
        var created = await CreateEventAsync("Edit review", At(tuesday, 9), At(tuesday, 10, 30));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), tuesday, CalendarMode.Week);

        var block = ItemBlock(window, "Edit review");
        var columnWidth = block.FindAncestorOfType<TimeGridPanel>()!.Bounds.Width;
        // One column right (Wednesday) and one hour down (10:00).
        await DragAsync(window, CentreOf(block, window), new Vector(columnWidth, 48));
        await WaitForAsync(() => calendar.DayColumns.Single(c => c.Date == tuesday.AddDays(1)).TimedItems.FirstOrDefault());

        var moved = await GetEventAsync(created.Id);
        Assert.Equal(At(tuesday.AddDays(1), 10), moved.StartAt);
        Assert.Equal(At(tuesday.AddDays(1), 11, 30), moved.EndAt); // length kept
        Assert.Equal(created.Version + 1, moved.Version);
        Assert.Null(calendar.SidePanel); // a drag is not a click
        Snapshot(window, "16-calendar-after-drag");
        window.Close();
    });

    [Fact]
    public Task Dragging_in_the_month_view_moves_the_event_by_whole_days() => Ui.Run(headless, async () =>
    {
        var monday = Monday(4);
        var created = await CreateEventAsync("Outreach planning", At(monday, 14), At(monday, 15));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), monday, CalendarMode.Month);

        var block = ItemBlock(window, "Outreach planning");
        var cell = block.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("monthCell"));
        // One row down = one week later, same time.
        await DragAsync(window, CentreOf(block, window), new Vector(0, cell.Bounds.Height));
        await WaitForAsync(() => calendar.MonthCells.Single(c => c.Date == monday.AddDays(7)).Items.FirstOrDefault());

        Assert.Equal(At(monday.AddDays(7), 14), (await GetEventAsync(created.Id)).StartAt);
        window.Close();
    });

    [Fact]
    public Task Filters_and_search_narrow_the_calendar() => Ui.Run(headless, async () =>
    {
        var day = Monday(5).AddDays(2);
        await CreateEventAsync("Leadership training", At(day, 8), At(day, 9), "Training");
        await CreateEventAsync("Weekly meeting", At(day, 10), At(day, 11), "Meeting");

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), day, CalendarMode.Day);
        string[] Titles() => [.. calendar.DayColumns.Single().TimedItems.Select(t => t.Item.Title)];
        Assert.Equal(2, Titles().Length);

        calendar.ShowFilters = true;
        calendar.EventTypeFilters.Single(f => f.Value == "Training").IsSelected = true;
        await WaitForAsync(() => Titles().Length == 1 ? calendar : null);
        Assert.Equal(["Leadership training"], Titles());
        Assert.Equal(1, calendar.ActiveFilterCount);
        Snapshot(window, "17-calendar-filters");

        calendar.ClearFiltersCommand.Execute(null);
        await WaitForAsync(() => Titles().Length == 2 ? calendar : null);
        calendar.SearchText = "weekly";
        Assert.Equal(["Weekly meeting"], Titles());
        window.Close();
    });

    [Fact]
    public Task Dashboard_entries_and_quick_create_open_the_calendar() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        var now = DateTimeOffset.UtcNow;
        var created = await CreateEventAsync($"Happening now {Guid.NewGuid():N}"[..20], now.AddSeconds(-1), now.AddSeconds(1),
            "Meeting", new CalendarAssignmentRequest(me.Id, "Organizer"));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var dashboard = Assert.IsType<DashboardViewModel>(shell.CurrentPage);
        await WaitForAsync(() => dashboard.IsLoading ? null : dashboard);

        var entry = dashboard.TodaySchedule.Single(i => i.Item.SourceId == created.Id);
        Assert.True(dashboard.CanViewOrganisation);
        Snapshot(window, "18-dashboard-personal");
        dashboard.ShowOrganisationCommand.Execute(null);
        Snapshot(window, "19-dashboard-organisation");

        await dashboard.OpenItemCommand.ExecuteAsync(entry);
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        Assert.Equal(created.Id, Assert.IsType<EventDetailsViewModel>(calendar.SidePanel).Event.Id);
        Assert.True(shell.PrimaryItems.Single(i => i.Title == "Calendar").IsSelected);

        await shell.CreateEventCommand.ExecuteAsync(null);
        Assert.IsType<EventEditorViewModel>(Assert.IsType<CalendarViewModel>(shell.CurrentPage).SidePanel);
        window.Close();
    });

    [Fact]
    public Task Team_members_see_the_calendar_read_only() => Ui.Run(headless, async () =>
    {
        var day = Monday(6).AddDays(1);
        var created = await CreateEventAsync("All-hands", At(day, 9), At(day, 10));
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        Assert.False(shell.CanCreateEvents);
        var calendar = await OpenCalendarAsync(shell, day, CalendarMode.Week);
        Assert.False(calendar.CanCreate);

        var item = calendar.DayColumns.Single(c => c.Date == day).TimedItems.Single().Item;
        Assert.False(item.CanReschedule);
        await DragAsync(window, CentreOf(ItemBlock(window, "All-hands"), window), new Vector(0, 96));
        Assert.Equal(At(day, 9), (await GetEventAsync(created.Id)).StartAt);
        window.Close();
    });

    [Fact]
    public Task Cancelling_from_the_details_panel_removes_the_event_from_the_calendar() => Ui.Run(headless, async () =>
    {
        var day = Monday(7).AddDays(3);
        var created = await CreateEventAsync("Postponed outreach", At(day, 15), At(day, 16));

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        Show(main);
        var calendar = await OpenCalendarAsync(await SignInAsync(main), day, CalendarMode.Day);
        await calendar.OpenItemCommand.ExecuteAsync(calendar.DayColumns.Single().TimedItems.Single().Item);
        var details = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);

        calendar.AskCancelEventCommand.Execute(details);
        Assert.True(details.IsConfirmingCancel);
        await calendar.ConfirmCancelEventCommand.ExecuteAsync(details);

        Assert.Null(calendar.SidePanel);
        Assert.Empty(calendar.DayColumns.Single().TimedItems);
        Assert.Equal("Cancelled", (await GetEventAsync(created.Id)).Status);
    });
}
