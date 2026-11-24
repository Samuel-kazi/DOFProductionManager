using System.Net.Http.Json;
using Avalonia.Controls;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Planning a week of devotions from the calendar: themes per day and who is sharing.</summary>
public class SessionWeekUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly DateOnly Monday = new(2035, 6, 4);

    private static List<CalendarItemViewModel> Mine(CalendarViewModel calendar, string name) =>
        [.. calendar.DayColumns.SelectMany(c => c.TimedItems).Select(t => t.Item).Where(i => i.Title.StartsWith(name))
            .DistinctBy(i => i.Item.Id).OrderBy(i => i.Item.StartAt)];

    [Fact]
    public Task A_week_of_devotions_is_planned_copied_and_edited_from_the_calendar() => Ui.Run(headless, async () =>
    {
        var name = $"Morning Devotion {Guid.NewGuid():N}"[..24];
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        calendar.AnchorDate = Monday.AddDays(2);
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);
        Assert.Contains(Find<Button>(window), b => b.Name == "NewEventMenuButton" && b.IsEffectivelyVisible);

        // Plan: one speaker for the week, a guest on Wednesday, Monday to Friday ticked by default.
        await calendar.PlanWeekCommand.ExecuteAsync(null);
        var editor = Assert.IsType<SessionWeekEditorViewModel>(calendar.SidePanel);
        Assert.Equal(Monday, editor.WeekMonday);
        Assert.Equal([true, true, true, true, true, false, false], editor.Days.Select(d => d.IsIncluded));
        editor.Name = name;
        editor.Speaker = "Pastor John";
        editor.Location = "Chapel";
        string[] themes = ["Walking in faith", "The power of prayer", "Grace that saves", "Love your neighbour", "Rest in Him"];
        for (var i = 0; i < 5; i++)
        {
            editor.Days[i].Theme = themes[i];
        }
        editor.Days[2].Speaker = "Rev. Mary";
        Assert.Equal("Pastor John", editor.Days[0].SpeakerHint);
        Snapshot(window, "69-plan-devotion-week");
        await calendar.SaveWeekCommand.ExecuteAsync(editor);

        Assert.Null(calendar.SidePanel);
        var items = Mine(calendar, name);
        Assert.Equal(themes.Select(t => $"{name}: {t}"), items.Select(i => i.Title));
        Assert.Contains("Sharing: Rev. Mary", items[2].ToolTip);
        Snapshot(window, "70-devotion-week-on-calendar");

        // The event shows who is sharing and opens the whole week.
        await calendar.OpenEventAsync(items[2].Item.SourceId);
        var details = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);
        Assert.Equal("Rev. Mary", details.Speaker);
        Assert.True(details.CanEditWeek);
        await calendar.EditWeekCommand.ExecuteAsync(details);
        var week = Assert.IsType<SessionWeekEditorViewModel>(calendar.SidePanel);
        Assert.False(week.IsNew);
        Assert.Equal("Rev. Mary", week.Days[2].Speaker);
        Assert.Null(week.Days[0].Speaker);
        week.Days[4].IsIncluded = false; // no devotion on Friday
        week.Days[1].Theme = "Praying without ceasing";
        await calendar.SaveWeekCommand.ExecuteAsync(week);
        items = Mine(calendar, name);
        Assert.Equal(4, items.Count);
        Assert.Equal($"{name}: Praying without ceasing", items[1].Title);

        // Next week: copy last week's time, place and speakers; themes are new.
        calendar.AnchorDate = Monday.AddDays(7);
        await calendar.PlanWeekCommand.ExecuteAsync(null);
        var next = Assert.IsType<SessionWeekEditorViewModel>(calendar.SidePanel);
        next.Name = name;
        await calendar.CopyLastWeekCommand.ExecuteAsync(next);
        Assert.StartsWith("Copied from the week of", next.Message);
        Assert.Equal(("Pastor John", "Chapel"), (next.Speaker, next.Location));
        Assert.Equal([true, true, true, true, false, false, false], next.Days.Select(d => d.IsIncluded));
        Assert.All(next.Days, d => Assert.Null(d.Theme));
        window.Close();
    });

    [Fact]
    public Task A_day_of_a_planned_week_invites_people_to_the_whole_week() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        await GoogleCalendarTests.ConnectAsync(factory, admin);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var monday = Monday.AddDays(21);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
        DateTimeOffset At(int day, int hour) =>
            new(TimeZoneInfo.ConvertTimeToUtc(monday.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), zone), TimeSpan.Zero);
        var name = $"Evening Devotion {Guid.NewGuid():N}"[..24];
        var week = await (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks", new SessionWeekRequest(name, monday, "MinistryEvent",
            "Chapel", null, "Pastor John",
            [.. Enumerable.Range(0, 5).Select(d => new SessionDayRequest(At(d, 18), At(d, 19), $"Theme {d + 1}"))]), Ct)).DataAsync<SessionWeekResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        await calendar.OpenEventAsync(week.Days[1].EventId);
        var invites = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel).Invites!;

        Assert.True(invites.IsWeek);
        Assert.Equal("Google Calendar · whole week", invites.HeadingText);
        Assert.Equal("Invite to the whole week…", invites.SendButtonText);
        Assert.Equal("This week has not been sent to Google Calendar.", invites.SummaryText);

        await invites.StartEditingCommand.ExecuteAsync(null);
        invites.People.Single(p => p.Id == member.Id).IsSelected = true;
        invites.ExtraEmails = "choir@example.org";
        await invites.SendCommand.ExecuteAsync(null);

        Assert.Equal("Synced", invites.Invite?.Status);
        Assert.Equal(week.Id, invites.Invite?.SessionWeekId);
        Assert.StartsWith("The whole week is on 2 people's Google Calendar", invites.SummaryText);
        Assert.StartsWith("One invite for the whole week has been sent", invites.Message);
        Snapshot(window, "71-google-invite-whole-week");

        // Any other day of the week shows the same invite.
        await calendar.OpenEventAsync(week.Days[3].EventId);
        var other = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel).Invites!;
        Assert.Equal(invites.Invite?.SessionWeekId, other.Invite?.SessionWeekId);
        Assert.Equal(2, other.Guests.Count);
        window.Close();
    });

    [Fact]
    public Task Weeks_and_events_over_several_days_are_drawn_as_one_bar() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var monday = Monday.AddDays(63); // 6 August 2035: this week and the next are both in the month grid
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
        DateTimeOffset At(int day, int hour) =>
            new(TimeZoneInfo.ConvertTimeToUtc(monday.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), zone), TimeSpan.Zero);
        var name = $"Bar Devotion {Guid.NewGuid():N}"[..20];
        var week = await (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks", new SessionWeekRequest(name, monday, "MinistryEvent",
            null, null, "Pastor John",
            [.. Enumerable.Range(0, 5).Select(d => new SessionDayRequest(At(d, 6), At(d, 7), $"Theme {d + 1}"))]), Ct)).DataAsync<SessionWeekResponse>();
        // A three-day camp from Saturday to Monday: it crosses into the next week row.
        var camp = $"Youth camp {Guid.NewGuid():N}"[..18];
        await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(camp, null, "MinistryEvent",
            At(5, 0), At(8, 0), true, "Campsite", null, "Normal", []), Ct);

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        calendar.AnchorDate = monday;
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Month);

        var row = calendar.MonthWeeks.Single(r => r.Cells[0].Date == monday);
        var devotion = row.Bars.Single(b => b.Title == name);
        Assert.Equal((0, 5, "06:00"), (devotion.Column, devotion.Span, devotion.TimeText));
        Assert.Equal(week.Days.Select(d => d.EventId), devotion.Segments.Select(s => s.Item!.Item.SourceId));
        Assert.Contains("Sharing: Pastor John", devotion.ToolTip);
        Assert.All(row.Cells, c => Assert.DoesNotContain(c.Items, i => i.Title.StartsWith(name))); // no separate highlights
        var campThisWeek = row.Bars.Single(b => b.Title == camp);
        Assert.Equal((5, 2, false, true), (campThisWeek.Column, campThisWeek.Span, campThisWeek.ContinuesBefore, campThisWeek.ContinuesAfter));
        var campNextWeek = calendar.MonthWeeks.Single(r => r.Cells[0].Date == monday.AddDays(7)).Bars.Single(b => b.Title == camp);
        Assert.Equal((0, 1, true, false), (campNextWeek.Column, campNextWeek.Span, campNextWeek.ContinuesBefore, campNextWeek.ContinuesAfter));
        Snapshot(window, "72-month-bars");

        // Clicking Wednesday's part of the week bar opens Wednesday's session.
        await calendar.OpenItemCommand.ExecuteAsync(devotion.Segments[2].Item);
        Assert.Equal(week.Days[2].EventId, Assert.IsType<EventDetailsViewModel>(calendar.SidePanel).Event.Id);
        calendar.CloseSidePanelCommand.Execute(null);

        // Week view: the camp is one bar in the all-day row; sessions stay in the time grid.
        calendar.AnchorDate = monday.AddDays(5);
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);
        var weekBar = calendar.AllDayBars.Single(b => b.Title == camp);
        Assert.Equal((5, 2), (weekBar.Column, weekBar.Span));
        Assert.All(calendar.DayColumns, c => Assert.DoesNotContain(c.AllDayItems, i => i.Title == camp));
        Snapshot(window, "73-week-all-day-bar");
        window.Close();
    });
}
