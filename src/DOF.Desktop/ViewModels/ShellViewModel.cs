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
        Func<TasksViewModel> tasks,
        Func<TeamViewModel> team,
        Func<SettingsViewModel> settings)
    {
        _session = session;
        _preferences = preferences;
        _clock = clock;
        Connection = connection;
        IsSidebarCollapsed = preferences.Current.SidebarCollapsed;

        var shortcutPrefix = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
        PrimaryItems =
        [
            new("Dashboard", "Icon.Dashboard", dashboard, $"{shortcutPrefix}1"),
            new("Calendar", "Icon.Calendar", calendar, $"{shortcutPrefix}2"),
            new("Projects", "Icon.Projects", projects, $"{shortcutPrefix}3"),
            new("Shows & Episodes", "Icon.Shows", () => new PlaceholderPageViewModel("Shows & Episodes", 5,
                "Shows, seasons and episodes with recording schedules, crews and production deadlines."), $"{shortcutPrefix}4"),
            new("Tasks", "Icon.Tasks", tasks, $"{shortcutPrefix}5"),
            new("Releases", "Icon.Releases", () => new PlaceholderPageViewModel("Releases", 6,
                "Distribution across YouTube, Spotify, television and other channels, with publication confirmation."), $"{shortcutPrefix}6"),
            new("Reminders", "Icon.Reminders", () => new PlaceholderPageViewModel("Reminders", 7,
                "Upcoming, delivered and failed reminders, and notification preferences."), $"{shortcutPrefix}7"),
            new("Reports", "Icon.Reports", () => new PlaceholderPageViewModel("Reports", 8,
                "Weekly, monthly, workload and publication reports with CSV export."), $"{shortcutPrefix}8"),
        ];
        FooterItems =
        [
            new("Team", "Icon.Team", team),
            new("Settings", "Icon.Settings", settings, $"{shortcutPrefix},"),
        ];
    }

    public ConnectionMonitor Connection { get; }

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

    public Task StartAsync() => NavigateAsync(PrimaryItems[0]);

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
            default:
                // Episodes and releases get their own screens in Phases 5–6.
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
    private Task SignOutAsync() => _session.LogoutAsync();
}
