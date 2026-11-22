using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Pages;

namespace DOF.Desktop.ViewModels;

/// <summary>The signed-in application frame: sidebar navigation, top bar and the current page.</summary>
public sealed partial class ShellViewModel : ViewModelBase, IRecordNavigator
{
    private readonly AuthSession _session;
    private readonly PreferencesStore _preferences;
    private readonly TimeProvider _clock;

    public ShellViewModel(
        AuthSession session,
        ConnectionMonitor connection,
        PreferencesStore preferences,
        TimeProvider clock,
        Func<DashboardViewModel> dashboard,
        Func<CalendarViewModel> calendar,
        Func<ProjectsViewModel> projects,
        Func<ShowsViewModel> shows,
        Func<TasksViewModel> tasks,
        Func<ReleasesViewModel> releases,
        Func<RemindersViewModel> reminders,
        Func<ReportsViewModel> reports,
        Func<TeamViewModel> team,
        Func<SettingsViewModel> settings,
        NotificationCenterViewModel notifications,
        SearchViewModel search)
    {
        _session = session;
        _preferences = preferences;
        _clock = clock;
        Connection = connection;
        Notifications = notifications;
        notifications.Navigator = this;
        Search = search;
        search.Navigator = this;
        IsSidebarCollapsed = preferences.Current.SidebarCollapsed;

        var shortcutPrefix = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
        PrimaryItems =
        [
            new("Dashboard", "Icon.Dashboard", dashboard, $"{shortcutPrefix}1"),
            new("Calendar", "Icon.Calendar", calendar, $"{shortcutPrefix}2"),
            new("Projects", "Icon.Projects", projects, $"{shortcutPrefix}3"),
            new("Shows & Episodes", "Icon.Shows", shows, $"{shortcutPrefix}4"),
            new("Tasks", "Icon.Tasks", tasks, $"{shortcutPrefix}5"),
            new("Releases", "Icon.Releases", releases, $"{shortcutPrefix}6"),
            new("Reminders", "Icon.Reminders", () =>
            {
                var page = reminders();
                page.Notifications = notifications;
                return page;
            }, $"{shortcutPrefix}7"),
            new("Reports", "Icon.Reports", reports, $"{shortcutPrefix}8"),
        ];
        FooterItems =
        [
            new("Team", "Icon.Team", team),
            new("Settings", "Icon.Settings", settings, $"{shortcutPrefix},"),
        ];
    }

    public ConnectionMonitor Connection { get; }

    public NotificationCenterViewModel Notifications { get; }

    public SearchViewModel Search { get; }

    private CancellationTokenSource? _background;

    public IReadOnlyList<NavigationItem> PrimaryItems { get; }

    public IReadOnlyList<NavigationItem> FooterItems { get; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; private set; }

    public string UserName => _session.CurrentUser?.FullName ?? string.Empty;

    public string UserEmail => _session.CurrentUser?.Email ?? string.Empty;

    public string UserInitials => TeamViewModel.Initials(UserName);

    public string TodayText
    {
        get
        {
            var zoneId = _session.CurrentUser?.TimeZone;
            var now = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)
                ? TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone)
                : _clock.GetUtcNow().ToLocalTime();
            return now.ToString("ddd d MMM yyyy");
        }
    }

    public IEnumerable<NavigationItem> AllItems => PrimaryItems.Concat(FooterItems);

    public bool CanCreateEvents => _session.CurrentUser?.Permissions.Contains("calendar.manage") == true;

    public async Task StartAsync()
    {
        StopBackgroundWork();
        _background = new CancellationTokenSource();
        _ = Notifications.RunAsync(_background.Token);
        await NavigateAsync(PrimaryItems[0]);
    }

    /// <summary>Stops notification polling (sign-out, or the shell leaving the screen).</summary>
    public void StopBackgroundWork()
    {
        _background?.Cancel();
        _background?.Dispose();
        _background = null;
    }

    [RelayCommand]
    public Task NavigateAsync(NavigationItem item) => ShowAsync(item, load: true);

    private async Task<PageViewModel> ShowAsync(NavigationItem item, bool load)
    {
        foreach (var other in AllItems)
        {
            other.IsSelected = ReferenceEquals(other, item);
        }

        var page = item.CreatePage();
        page.Navigator = this;
        CurrentPage = page;
        if (load)
        {
            await page.LoadAsync();
        }
        return page;
    }

    private NavigationItem CalendarItem => PrimaryItems.Single(i => i.Title == "Calendar");
    private NavigationItem ProjectsItem => PrimaryItems.Single(i => i.Title == "Projects");
    private NavigationItem TasksItem => PrimaryItems.Single(i => i.Title == "Tasks");
    private NavigationItem ReleasesItem => PrimaryItems.Single(i => i.Title == "Releases");
    private NavigationItem ShowsItem => PrimaryItems.Single(i => i.Title == "Shows & Episodes");

    public bool CanCreateShows => _session.CurrentUser?.Permissions.Contains("episodes.manage") == true;

    public bool CanScheduleReleases => _session.CurrentUser?.Permissions.Contains("releases.schedule") == true;

    [RelayCommand(CanExecute = nameof(CanCreateShows))]
    private async Task CreateShowAsync()
    {
        var shows = (ShowsViewModel)await ShowAsync(ShowsItem, load: true);
        await shows.NewShowAsync();
    }

    [RelayCommand(CanExecute = nameof(CanScheduleReleases))]
    private async Task CreateReleaseFromMenuAsync()
    {
        var releases = (ReleasesViewModel)await ShowAsync(ReleasesItem, load: true);
        await releases.NewReleaseAsync(null);
    }

    public bool CanCreateProjects => _session.CurrentUser?.Permissions.Contains("projects.create") == true;

    /// <summary>Opens the original record behind a calendar or dashboard entry.</summary>
    public async Task OpenRecordAsync(string sourceType, Guid sourceId)
    {
        switch (sourceType)
        {
            case "CalendarEvent":
                var calendar = (CalendarViewModel)await ShowAsync(CalendarItem, load: false);
                await calendar.OpenEventAsync(sourceId);
                break;
            case "Project":
                await ((ProjectsViewModel)await ShowAsync(ProjectsItem, load: false)).OpenProjectAsync(sourceId);
                break;
            case "Milestone":
                await ((ProjectsViewModel)await ShowAsync(ProjectsItem, load: false)).OpenMilestoneByIdAsync(sourceId);
                break;
            case "Task":
                var tasks = (TasksViewModel)await ShowAsync(TasksItem, load: true);
                await tasks.OpenTaskAsync(sourceId);
                break;
            case "Show":
                await ((ShowsViewModel)await ShowAsync(ShowsItem, load: false)).OpenShowAsync(sourceId);
                break;
            case "Episode":
                await ((ShowsViewModel)await ShowAsync(ShowsItem, load: false)).OpenEpisodeAsync(sourceId);
                break;
            case "Release":
                await ((ReleasesViewModel)await ShowAsync(ReleasesItem, load: true)).OpenReleaseAsync(sourceId);
                break;
            default:
                AppLog.Warning($"No screen yet for {sourceType} {sourceId}");
                break;
        }
    }

    /// <summary>Quick create / project page: opens the Tasks page with a new task (optionally in a project).</summary>
    [RelayCommand]
    public async Task CreateTaskAsync(Guid? projectId = null)
    {
        var tasks = (TasksViewModel)await ShowAsync(TasksItem, load: true);
        await tasks.NewTaskAsync(projectId);
    }

    public async Task CreateReleaseAsync(Guid episodeId)
    {
        var releases = (await ShowAsync(ReleasesItem, load: true)) as ReleasesViewModel;
        await releases!.NewReleaseAsync(episodeId);
    }

    public async Task CreateEpisodeTaskAsync(Guid episodeId)
    {
        var tasks = (TasksViewModel)await ShowAsync(TasksItem, load: true);
        await tasks.NewTaskAsync(null, episodeId);
    }

    [RelayCommand(CanExecute = nameof(CanCreateProjects))]
    private async Task CreateProjectAsync()
    {
        var projects = (ProjectsViewModel)await ShowAsync(ProjectsItem, load: true);
        await projects.NewProjectAsync();
    }

    public async Task ShowTasksForAsync(Guid userId, string fullName)
    {
        var tasks = (TasksViewModel)await ShowAsync(TasksItem, load: false);
        await tasks.ShowPersonAsync(userId, fullName);
    }

    /// <summary>Quick create: opens the calendar with a new event.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateEvents))]
    public async Task CreateEventAsync(DateOnly? date = null)
    {
        var calendar = (CalendarViewModel)await ShowAsync(CalendarItem, load: true);
        await calendar.NewEventAsync(date);
    }

    [RelayCommand]
    private Task NavigateToIndexAsync(int index) =>
        index >= 0 && index < PrimaryItems.Count ? NavigateAsync(PrimaryItems[index]) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenSettingsAsync() => NavigateAsync(FooterItems.Single(i => i.Title == "Settings"));

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        _preferences.Update(p => p with { SidebarCollapsed = IsSidebarCollapsed });
    }

    [RelayCommand]
    private Task CheckConnectionAsync() => Connection.CheckAsync();

    [RelayCommand]
    private Task SignOutAsync()
    {
        StopBackgroundWork();
        return _session.LogoutAsync();
    }
}
