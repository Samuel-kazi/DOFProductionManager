using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Calendar;
using DOF.Desktop.ViewModels.Pages;
using static DOF.IntegrationTests.Desktop.Ui;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>A weekly meeting set up once from the event editor, then stopped from a date on.</summary>
public class RepeatingEventUiTests(DofApiFactory factory, HeadlessSession headless)
{
    [Fact]
    public Task A_weekly_meeting_is_created_once_and_stopped_from_a_date() => Ui.Run(headless, async () =>
    {
        var monday = new DateOnly(2036, 9, 1);
        var title = $"Prayer meeting {Guid.NewGuid():N}"[..22];
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.NavigateCommand.ExecuteAsync(shell.PrimaryItems.Single(i => i.Title == "Calendar"));
        var calendar = Assert.IsType<CalendarViewModel>(shell.CurrentPage);
        calendar.AnchorDate = monday;
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Month);

        await calendar.NewEventCommand.ExecuteAsync(monday.AddDays(2)); // a Wednesday
        var editor = Assert.IsType<EventEditorViewModel>(calendar.SidePanel);
        editor.Title = title;
        editor.SelectedRepeat = EventEditorViewModel.RepeatChoices.Single(c => c.Label == "Every week");
        Assert.True(editor.ShowRepeatDays);
        Assert.Equal([DayOfWeek.Wednesday], editor.WeekdayOptions.Where(o => o.IsSelected).Select(o => o.Day)); // the start day
        editor.WeekdayOptions.Single(o => o.Day == DayOfWeek.Friday).IsSelected = true;
        editor.RepeatUntil = monday.AddDays(25).ToDateTime(TimeOnly.MinValue);
        Snapshot(window, "74-repeating-event-editor");
        await calendar.SaveEventCommand.ExecuteAsync(editor);

        var details = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);
        Assert.Equal("Every week on Wed and Fri until 26 Sep 2036", details.RepeatText);
        var mine = calendar.MonthCells.SelectMany(c => c.Items).Where(i => i.Title == title).DistinctBy(i => i.Item.Id).ToList();
        Assert.Equal(8, mine.Count); // Wednesdays and Fridays, 3 to 26 September
        Assert.All(mine, i => Assert.True(i.Item.IsRepeating));

        // Stop it from the third week.
        var third = mine.OrderBy(i => i.Start).ElementAt(4);
        await calendar.OpenItemCommand.ExecuteAsync(third);
        var thirdDetails = Assert.IsType<EventDetailsViewModel>(calendar.SidePanel);
        Assert.True(thirdDetails.IsInSeries);
        calendar.AskCancelEventCommand.Execute(thirdDetails);
        await calendar.ConfirmCancelFollowingCommand.ExecuteAsync(thirdDetails);
        Assert.Equal(4, calendar.MonthCells.SelectMany(c => c.Items).Where(i => i.Title == title).DistinctBy(i => i.Item.Id).Count());
        window.Close();
    });
}
