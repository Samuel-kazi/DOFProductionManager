using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.ViewModels.Releases;
using DOF.Desktop.Views.Pages;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Releases in the desktop client, against the real API.</summary>
public class ReleaseUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static DateOnly Day(int n) => new DateOnly(2034, 11, 6).AddDays(n);
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

    private static async Task<EpisodeResponse> EpisodeAsync(HttpClient admin, string showName)
    {
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest(showName, null, "Recurring", null, 60), Ct)).DataAsync<ShowResponse>();
        var season = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/seasons",
            new CreateSeasonRequest(null, null, null, null), Ct)).DataAsync<SeasonResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes",
            new CreateEpisodeRequest("Hope", null, null, null, null, null, null, null, null, null), Ct)).DataAsync<EpisodeResponse>();
        await admin.PatchAsJsonAsync($"/api/v1/episodes/{episode.Id}/status", new ChangeEpisodeStatusRequest("ReadyForRelease"), Ct);
        return episode;
    }

    [Fact]
    public Task A_release_is_scheduled_from_the_episode_and_published_only_with_confirmation() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var episode = await EpisodeAsync(admin, $"Release show {Guid.NewGuid():N}"[..21]);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);

        // From the episode's distribution panel to a new release with the episode chosen.
        await shell.OpenRecordAsync("Episode", episode.Id);
        var shows = Assert.IsType<ShowsViewModel>(shell.CurrentPage);
        Assert.True(shows.EpisodeDetail!.CanScheduleReleases);
        Assert.Equal("No releases planned yet.", shows.EpisodeDetail.DistributionSummary);
        await shows.NewReleaseCommand.ExecuteAsync(null);
        var releases = Assert.IsType<ReleasesViewModel>(shell.CurrentPage);
        Render(window);
        Assert.Single(Find<ReleasesView>(window));
        var editor = Assert.IsType<ReleaseEditorViewModel>(releases.SidePanel);
        Assert.Equal(episode.Id, editor.SelectedEpisode!.Id);
        editor.SelectedChannel = editor.Channels.Single(c => c.Label == "YouTube");
        editor.Date = DateTime.Today.AddDays(-1);
        editor.Time = new TimeSpan(18, 0, 0);
        editor.SelectedPerson = editor.People.Single(p => p.Label == "System Administrator");
        Snapshot(window, "40-release-editor");
        await releases.SaveReleaseCommand.ExecuteAsync(editor);

        // Its time has passed: flagged overdue, not published.
        var details = Assert.IsType<ReleaseDetailsViewModel>(releases.SidePanel);
        Assert.Equal(("Scheduled", true, "Overdue"), (details.Release.Status, details.IsOverdue, details.StatusText));
        Assert.Null(details.Release.ActualPublishedAt);

        // Publishing needs the confirmation box.
        releases.BeginPublishCommand.Execute(details);
        Assert.True(details.IsPublishing);
        await releases.ConfirmActionCommand.ExecuteAsync(details);
        Assert.Equal("Tick the box to confirm the release has actually been published.", details.ErrorMessage);
        details.PublishedDate = DateTime.Today;
        details.PublishedTime = new TimeSpan(0, 5, 0);
        details.PublishedUrl = "https://youtu.be/hope";
        details.Confirmed = true;
        Snapshot(window, "41-release-publish");
        await releases.ConfirmActionCommand.ExecuteAsync(details);
        details = await WaitForAsync(() => releases.SidePanel is ReleaseDetailsViewModel d && d.Release.Status == "Published" ? d : null);
        Assert.NotNull(details.Release.ScheduledAt);
        Assert.Contains(details.History, h => h.Summary.StartsWith("Publication confirmed: published "));
        Snapshot(window, "42-release-published");

        // The episode now reads as published, with the channel in its distribution panel.
        await releases.OpenEpisodeCommand.ExecuteAsync(details);
        shows = Assert.IsType<ShowsViewModel>(shell.CurrentPage);
        var detail = shows.EpisodeDetail!;
        Assert.Equal("Published", detail.Episode.Status);
        Assert.Equal("1 of 1 channels published", detail.DistributionSummary);
        Assert.Equal("Published", Assert.Single(detail.Releases).StatusText);
        Render(window);
        Snapshot(window, "43-episode-distribution");
        window.Close();
    });

    [Fact]
    public Task Release_views_flag_missed_releases_and_calendar_drags_and_postpones_move_them() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var channels = (await (await admin.GetAsync("/api/v1/distribution-channels", Ct)).DataAsync<List<DistributionChannelResponse>>())
            .ToDictionary(c => c.Name, c => c.Id);
        var episode = await EpisodeAsync(admin, "Views show");
        var missed = await (await admin.PostAsJsonAsync("/api/v1/releases", new CreateReleaseRequest(
            episode.Id, channels["Facebook"], DateTimeOffset.UtcNow.AddHours(-3), "Scheduled", null), Ct)).DataAsync<ReleaseResponse>();
        var tv = await (await admin.PostAsJsonAsync("/api/v1/releases", new CreateReleaseRequest(
            episode.Id, channels["Television"], At(Day(2), 20), "Scheduled", null), Ct)).DataAsync<ReleaseResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var releases = await PageAsync<ReleasesViewModel>(shell, "Releases");
        releases.SelectedView = releases.Views.Single(v => v.View == "Overdue");
        var row = await WaitForAsync(() => releases.IsLoading ? null : releases.Items.FirstOrDefault(r => r.Id == missed.Id));
        Assert.True(row.IsOverdue);
        Assert.DoesNotContain(releases.Items, r => r.Id == tv.Id);
        Assert.NotNull(releases.UnpublishedSummary);
        Snapshot(window, "44-releases-overdue");

        // The TV release is on the calendar at its own time and drags to a new one.
        var calendar = await PageAsync<CalendarViewModel>(shell, "Calendar");
        calendar.AnchorDate = Day(2);
        await calendar.SetModeCommand.ExecuteAsync(CalendarMode.Week);
        var item = calendar.DayColumns.Single(c => c.Date == Day(2)).TimedItems.Single(t => t.Item.Item.SourceId == tv.Id);
        Assert.Equal("Brush.Cat.Release", item.Item.CategoryBrushKey);
        Snapshot(window, "45-calendar-with-releases");
        await calendar.RescheduleAsync(item.Item, At(Day(3), 21).ToOffset(TimeSpan.FromHours(3)).DateTime);
        Assert.Equal(At(Day(3), 21), (await (await admin.GetAsync($"/api/v1/releases/{tv.Id}", Ct)).DataAsync<ReleaseResponse>()).ScheduledAt);

        // Opening it goes to the release; postponing needs a reason and moves it off the calendar.
        var moved = calendar.DayColumns.Single(c => c.Date == Day(3)).TimedItems.Single(t => t.Item.Item.SourceId == tv.Id);
        await calendar.OpenItemCommand.ExecuteAsync(moved.Item);
        releases = Assert.IsType<ReleasesViewModel>(shell.CurrentPage);
        var details = Assert.IsType<ReleaseDetailsViewModel>(releases.SidePanel);
        Assert.Equal(tv.Id, details.Release.Id);
        releases.BeginPostponeCommand.Execute(details);
        await releases.ConfirmActionCommand.ExecuteAsync(details);
        Assert.NotNull(details.ErrorMessage);
        details.ActionReason = "Football overran";
        await releases.ConfirmActionCommand.ExecuteAsync(details);
        details = await WaitForAsync(() => releases.SidePanel is ReleaseDetailsViewModel d && d.Release.Status == "Postponed" ? d : null);
        Assert.Equal("Postponed: Football overran", details.Reason);
        Assert.Contains(details.History, h => h.Summary.StartsWith("Postponed from ") && h.Summary.EndsWith("further notice: Football overran"));
        window.Close();
    });

    [Fact]
    public Task Administrators_manage_distribution_channels_in_settings() => Ui.Run(headless, async () =>
    {
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        await shell.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Assert.True(settings.CanManageChannels);
        await WaitForAsync(() => settings.Channels.Count > 0 ? settings : null);
        Assert.Contains(settings.Channels, c => c.Name == "Spotify");

        settings.NewChannelName = $"Podcast {Guid.NewGuid():N}"[..14];
        settings.NewChannelType = "Audio";
        var name = settings.NewChannelName;
        await settings.AddChannelCommand.ExecuteAsync(null);
        var added = settings.Channels.Single(c => c.Name == name);
        await settings.ToggleChannelCommand.ExecuteAsync(added);
        Assert.False(settings.Channels.Single(c => c.Name == name).IsActive);
        Snapshot(window, "46-settings-channels");
        window.Close();
    });
}
