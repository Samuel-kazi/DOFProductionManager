using Avalonia.Controls;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;
using static DOF.IntegrationTests.Desktop.Ui;
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
}
