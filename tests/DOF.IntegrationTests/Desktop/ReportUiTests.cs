using System.Net.Http.Json;
using System.Text;
using Avalonia.Controls;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using DOF.Desktop.ViewModels.Work;
using DOF.Desktop.Views.Pages;
using static DOF.IntegrationTests.Desktop.Ui;
using static DOF.IntegrationTests.TestApi;
using CalendarMode = DOF.Desktop.ViewModels.Calendar.CalendarMode;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Reports, global search and profile settings in the desktop client, against the real API.</summary>
public class ReportUiTests(DofApiFactory factory, HeadlessSession headless)
{
    private static async Task<T> PageAsync<T>(ShellViewModel shell, string title) where T : PageViewModel
    {
        await shell.NavigateCommand.ExecuteAsync(shell.AllItems.Single(i => i.Title == title));
        var page = Assert.IsType<T>(shell.CurrentPage);
        await WaitForAsync(() => page.IsLoading ? null : page);
        return page;
    }

    [Fact]
    public Task Reports_show_as_tables_open_their_records_and_export_csv() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var title = $"Report row {Guid.NewGuid():N}"[..20];
        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest(title, null, null, DateTimeOffset.UtcNow.AddDays(-2), "High", null, null), Ct)).DataAsync<TaskResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main);
        var reports = await PageAsync<ReportsViewModel>(shell, "Reports");
        await WaitForAsync(() => reports.Report);
        Assert.Equal("weekly", reports.Report!.Key);
        Render(window);
        Assert.Single(Find<ReportsView>(window));
        Snapshot(window, "60-report-weekly");

        reports.SelectedReport = reports.Reports.Single(r => r.Key == "overdue-tasks");
        var report = await WaitForAsync(() => reports.Report?.Key == "overdue-tasks" && !reports.IsLoading ? reports.Report : null);
        var index = report.Links.ToList().FindIndex(l => l?.SourceId == task.Id);
        Assert.Equal(title, report.Rows[index][0]);
        Render(window);
        var table = window.FindControl<Grid>("Table") ?? Find<Grid>(window).Single(g => g.Name == "Table");
        Assert.Equal((report.Rows.Count + 1) * report.Columns.Count, table.Children.Count);
        Snapshot(window, "61-report-overdue");

        // CSV export of what is on screen.
        using var csv = new MemoryStream();
        await reports.ExportCsvAsync(csv);
        var bytes = csv.ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.StartsWith("Task,Due,Days overdue", Encoding.UTF8.GetString(bytes[3..]));
        Assert.Equal("Exported.", reports.ExportMessage);

        // Rows open their record.
        await reports.OpenRowAsync(index);
        var tasks = Assert.IsType<TasksViewModel>(shell.CurrentPage);
        Assert.Equal(task.Id, Assert.IsType<TaskDetailsViewModel>(tasks.SidePanel).Task.Id);
        window.Close();
    });

    [Fact]
    public Task Global_search_opens_any_record_and_people_without_reports_view_are_told() => Ui.Run(headless, async () =>
    {
        var admin = await factory.AdminClientAsync();
        var (member, _) = await factory.UserClientAsync(DOF.Domain.Rules.SystemRoles.TeamMember);
        var term = $"quokka{Guid.NewGuid():N}"[..12];
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows", new CreateShowRequest($"Show {term}", null, "OneOff", null, null), Ct))
            .DataAsync<ShowResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/episodes",
            new CreateEpisodeRequest($"Episode {term}", null, null, null, null, null, null, null, null, null), Ct)).DataAsync<EpisodeResponse>();

        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        shell.Search.Text = term;
        await shell.Search.SearchCommand.ExecuteAsync(null);
        Assert.True(shell.Search.IsOpen);
        Assert.Contains(shell.Search.Results, r => r.SourceType == "Show" && r.Id == show.Id);
        Render(window);
        Snapshot(window, "62-global-search");
        await shell.Search.OpenCommand.ExecuteAsync(shell.Search.Results.Single(r => r.SourceType == "Episode"));
        var shows = Assert.IsType<ShowsViewModel>(shell.CurrentPage);
        Assert.Equal(episode.Id, shows.EpisodeDetail!.Episode.Id);
        Assert.False(shell.Search.IsOpen);

        var reports = await PageAsync<ReportsViewModel>(shell, "Reports");
        Assert.False(reports.CanView);
        Assert.Contains("reports.view", reports.ErrorMessage);
        window.Close();
    });

    [Fact]
    public Task Settings_change_the_timezone_and_the_default_calendar_view() => Ui.Run(headless, async () =>
    {
        var (member, _) = await factory.UserClientAsync(DOF.Domain.Rules.SystemRoles.TeamMember);
        using var harness = new DesktopHarness(factory);
        var main = harness.Get<MainWindowViewModel>();
        var window = Show(main);
        var shell = await SignInAsync(main, member.Email, member.Password);
        await shell.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        Assert.Contains("Europe/London", settings.TimeZones);
        settings.SelectedTimeZone = "Europe/London";
        settings.EditableName = "Ruth Achieng";
        await settings.SaveProfileCommand.ExecuteAsync(null);
        Assert.Equal("Saved. Times are now shown in Europe/London.", settings.ProfileMessage);
        Assert.Equal("Europe/London", harness.Get<UserClock>().Zone.Id);
        Assert.Equal("Ruth Achieng", shell.UserName);

        settings.DefaultCalendarView = "Week";
        Snapshot(window, "63-settings-profile");
        var calendar = await PageAsync<CalendarViewModel>(shell, "Calendar");
        Assert.Equal(CalendarMode.Week, calendar.Mode);
        settings.DefaultCalendarView = "Month";
        window.Close();
    });
}
