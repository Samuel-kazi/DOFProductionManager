using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.ViewModels.Shows;
using DOF.Desktop.ViewModels.Work;
using DOF.Desktop.Views.Pages;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Shows, seasons and episodes in the desktop client, against the real API.</summary>
public class ShowUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static DateOnly Day(int n) => new DateOnly(2034, 6, 5).AddDays(n);
    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static async Task<T> PageAsync<T>(ShellViewModel shell, string title) where T : PageViewModel
    {
        await shell.NavigateCommand.ExecuteAsync(shell.AllItems.Single(i => i.Title == title));
        var page = Assert.IsType<T>(shell.CurrentPage);
        await WaitForAsync(() => page.IsLoading ? null : page);
        Assert.False(page.HasError, page.ErrorMessage);
        return page;
    }

    [Fact]
    public Task A_show_season_and_episode_are_built_and_crewed_from_the_shows_page() => Ui.Run(headless, async () =>
    {
        var (editor, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var shows = await PageAsync<ShowsViewModel>(shell, "Shows & Episodes");
        Render(window);
        Assert.Single(Find<ShowsView>(window));
        Assert.True(shows.CanCreate);

        // Show
        await shows.NewShowCommand.ExecuteAsync(null);
        var showEditor = Assert.IsType<ShowEditorViewModel>(shows.SidePanel);
        var name = $"Faith Talk {Guid.NewGuid():N}"[..19];
        showEditor.Name = name;
        showEditor.ShowType = "Recurring";
        showEditor.DurationMinutes = 90;
        showEditor.SelectedProducer = showEditor.Producers.Single(p => p.Label == "System Administrator");
        Snapshot(window, "30-show-editor");
        await shows.SaveShowCommand.ExecuteAsync(showEditor);
        var show = Assert.IsType<ShowDetailViewModel>(shows.ShowDetail);
        Assert.Equal(name, show.Name);

        // Season
        shows.NewSeasonCommand.Execute(null);
        var seasonEditor = Assert.IsType<SeasonEditorViewModel>(shows.SidePanel);
        Assert.Equal(1, seasonEditor.Number);
        seasonEditor.Name = "Beginnings";
        await shows.SaveSeasonCommand.ExecuteAsync(seasonEditor);
        var season = shows.ShowDetail!.Groups.First(g => g.Season is not null);
        Assert.Equal("Season 1: Beginnings", season.Heading);

        // Episode, numbered next in the season, with the show's usual length
        await shows.NewEpisodeCommand.ExecuteAsync(season);
        var episodeEditor = Assert.IsType<EpisodeEditorViewModel>(shows.SidePanel);
        Assert.Equal(season.Season!.Id, episodeEditor.SelectedSeason.Id);
        Assert.Equal(90, episodeEditor.DurationMinutes);
        episodeEditor.Title = "Walking in grace";
        episodeEditor.HasRecording = true;
        episodeEditor.RecordingDate = Day(2).ToDateTime(TimeOnly.MinValue);
        episodeEditor.RecordingTime = new TimeSpan(9, 0, 0);
        episodeEditor.Location = "Studio A";
        episodeEditor.EditingDue = Day(5).ToDateTime(TimeOnly.MinValue);
        episodeEditor.ReviewDue = Day(7).ToDateTime(TimeOnly.MinValue);
        Snapshot(window, "31-episode-editor");
        await shows.SaveEpisodeCommand.ExecuteAsync(episodeEditor);

        var episode = Assert.IsType<EpisodeDetailViewModel>(shows.EpisodeDetail);
        Assert.True(shows.ShowingEpisode);
        Assert.Equal((1, At(Day(2), 9), At(Day(2), 9).AddMinutes(90)),
            (episode.Episode.EpisodeNumber, episode.Episode.RecordingStart, episode.Episode.RecordingEnd));
        Assert.Contains(episode.Information, i => i.Label == "Location" && i.Value == "Studio A");

        // Crew
        episode.NewPerson = episode.People.Single(p => p.Id == editor.Id);
        episode.NewResponsibility = "Editor";
        await shows.AssignCommand.ExecuteAsync(null);
        episode = shows.EpisodeDetail!;
        Assert.Contains(episode.Crew, a => a.UserId == editor.Id && a.Responsibility == "Editor");
        Assert.Equal("Test User", episode.Information.Single(i => i.Label == "Editor").Value);

        // Status through the picker: skipping to editing records the recording; history keeps it.
        Assert.DoesNotContain("Published", episode.StatusOptions);
        episode.SelectedStatus = "Editing";
        episode = await WaitForAsync(() => shows.EpisodeDetail is { Episode.Status: "Editing" } d ? d : null);
        Assert.Equal(["Done", "Done", "Current"], episode.Stages.Take(3).Select(s => s.State));
        Assert.Contains(episode.History, h => h.Summary == "Status changed from Planned to Editing");
        Assert.Contains(episode.History, h => h.Summary == "Test User assigned as Editor");
        Render(window);
        Snapshot(window, "32-episode-detail");

        // Back to the show: the episode sits under its season with its next deadline.
        await shows.BackCommand.ExecuteAsync(null);
        var row = Assert.Single(shows.ShowDetail!.Groups.First(g => g.Season is not null).Episodes);
        Assert.Equal("S1 E1 · Walking in grace", row.Title);
        Assert.StartsWith("Editing due", row.DeadlineText);
        Snapshot(window, "33-show-detail");

        await shows.BackCommand.ExecuteAsync(null);
        var card = Assert.Single(shows.Shows, s => s.Name == name);
        Assert.Equal("1 season · 1 episode · 1 in production", card.CountsText);
        Snapshot(window, "34-shows-list");
        window.Close();
    });

    [Fact]
    public Task Episode_recordings_open_from_the_calendar_and_drag_to_a_new_time() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest("Calendar show", null, "Recurring", null, 60), Ct)).DataAsync<ShowResponse>();
        var season = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/seasons",
            new CreateSeasonRequest(null, null, null, null), Ct)).DataAsync<SeasonResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes", new CreateEpisodeRequest(
            "On the calendar", null, null, null, At(Day(22), 10), null, "Studio B", Day(23), null, null), Ct)).DataAsync<EpisodeResponse>();
        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(
            "Book the studio", null, null, At(Day(21), 15), "Normal", null, null, episode.Id), Ct)).DataAsync<TaskResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var calendar = await PageAsync<CalendarViewModel>(shell, "Calendar");
        calendar.AnchorDate = Day(22);
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);

        var recording = calendar.DayColumns.Single(c => c.Date == Day(22)).TimedItems.Single(t => t.Item.Item.SourceId == episode.Id);
        Assert.Equal((10 * 60, "Brush.Cat.Recording"), (recording.StartMinute, recording.Item.CategoryBrushKey));
        Assert.True(recording.Item.CanReschedule);
        Assert.Contains(calendar.DayColumns.Single(c => c.Date == Day(23)).AllDayItems,
            i => i.Item.SourceId == episode.Id && i.Item.EventType == "EditingDeadline");
        Snapshot(window, "35-calendar-with-episodes");

        // Dragging moves the episode's recording (same length) and a task keeps its episode.
        await calendar.RescheduleAsync(recording.Item, At(Day(24), 14).ToOffset(TimeSpan.FromHours(3)).DateTime);
        var taskItem = calendar.DayColumns.Single(c => c.Date == Day(21)).TimedItems.Single(t => t.Item.Item.SourceId == task.Id);
        await calendar.RescheduleAsync(taskItem.Item, At(Day(21), 16).ToOffset(TimeSpan.FromHours(3)).DateTime);
        var moved = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}", Ct)).DataAsync<EpisodeResponse>();
        Assert.Equal((At(Day(24), 14), At(Day(24), 15)), (moved.RecordingStart, moved.RecordingEnd));
        Assert.Equal(episode.Id, Assert.Single(moved.Tasks).Episode!.Id);
        Assert.Equal(At(Day(21), 16), moved.Tasks[0].DueAt);

        // Opening the recording goes to the episode.
        var movedItem = calendar.DayColumns.Single(c => c.Date == Day(24)).TimedItems.Single(t => t.Item.Item.SourceId == episode.Id);
        await calendar.OpenItemCommand.ExecuteAsync(movedItem.Item);
        var shows = Assert.IsType<ShowsViewModel>(shell.CurrentPage);
        Assert.Equal(episode.Id, shows.EpisodeDetail!.Episode.Id);
        Assert.Equal("Book the studio", Assert.Single(shows.EpisodeDetail.Tasks).Title);

        // New tasks from the episode page belong to the episode.
        await shows.NewTaskCommand.ExecuteAsync(null);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        var editor = Assert.IsType<TaskEditorViewModel>(tasks.SidePanel);
        Assert.Equal(episode.Id, editor.EpisodeId);
        Assert.Null(editor.ProjectId);
        window.Close();
    });

    [Fact]
    public Task My_episodes_appear_on_the_dashboard_and_the_shows_page() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var (member, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest("Crew show", null, "LimitedSeries", null, null), Ct)).DataAsync<ShowResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/episodes", new CreateEpisodeRequest(
            "Special", null, null, null, null, null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2), null, null), Ct))
            .DataAsync<EpisodeResponse>();
        await admin.PostAsJsonAsync($"/api/v1/episodes/{episode.Id}/assignments", new AssignEpisodeRequest(member.Id, "Presenter"), Ct);

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        var dashboard = Assert.IsType<DashboardViewModel>(shell.CurrentPage);
        var mine = Assert.Single(dashboard.MyEpisodes);
        Assert.True(mine.IsOverdue);
        Snapshot(window, "36-dashboard-my-episodes");

        var shows = await PageAsync<ShowsViewModel>(shell, "Shows & Episodes");
        Assert.False(shows.CanCreate);
        shows.OnlyMyEpisodes = true;
        var row = await WaitForAsync(() => shows.MyEpisodes.FirstOrDefault());
        Assert.Equal(episode.Id, row.Id);
        await shows.OpenEpisodeRowCommand.ExecuteAsync(row);
        var detail = shows.EpisodeDetail!;
        Assert.False(detail.CanEdit);
        Assert.True(detail.CanChangeStatus);
        Snapshot(window, "37-episode-as-crew");
        window.Close();
    });
}
