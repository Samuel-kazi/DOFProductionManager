using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Pages;

namespace DOF.Desktop.ViewModels;

/// <summary>The signed-in application frame: sidebar navigation, top bar and the current page.</summary>
public sealed partial class ShellViewModel : ViewModelBase
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
            new("Calendar", "Icon.Calendar", () => new PlaceholderPageViewModel("Calendar", 3,
                "Month, week, day and agenda views of every recording, deadline, milestone, release and event."), $"{shortcutPrefix}2"),
            new("Projects", "Icon.Projects", () => new PlaceholderPageViewModel("Projects", 4,
                "Projects, members, milestones and timelines."), $"{shortcutPrefix}3"),
            new("Shows & Episodes", "Icon.Shows", () => new PlaceholderPageViewModel("Shows & Episodes", 5,
                "Shows, seasons and episodes with recording schedules, crews and production deadlines."), $"{shortcutPrefix}4"),
            new("Tasks", "Icon.Tasks", () => new PlaceholderPageViewModel("Tasks", 4,
                "Personal and team tasks in list and board views."), $"{shortcutPrefix}5"),
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

    public Task StartAsync() => NavigateAsync(PrimaryItems[0]);

    [RelayCommand]
    public async Task NavigateAsync(NavigationItem item)
    {
        foreach (var other in AllItems)
        {
            other.IsSelected = ReferenceEquals(other, item);
        }

        var page = item.CreatePage();
        CurrentPage = page;
        await page.LoadAsync();
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
