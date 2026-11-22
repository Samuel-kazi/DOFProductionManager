using DOF.Desktop.Services.Api;
using DOF.Desktop.Services.Security;
using DOF.Desktop.ViewModels;
using DOF.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.Desktop.Services;

/// <summary>Composition root for the desktop client.</summary>
public static class DesktopServices
{
    /// <param name="innerHandler">The network handler; tests pass an in-memory server handler.</param>
    public static ServiceProvider Build(
        DesktopSettings settings,
        ISecureStore secureStore,
        PreferencesStore preferences,
        IThemeService? theme = null,
        HttpMessageHandler? innerHandler = null)
    {
        var network = innerHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        HttpClient Client(HttpMessageHandler handler, TimeSpan timeout) =>
            new(handler, disposeHandler: false) { BaseAddress = settings.ApiBaseUrl, Timeout = timeout };

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(secureStore);
        services.AddSingleton(preferences);
        services.AddSingleton(theme ?? new ThemeService());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<UserClock>();
        services.AddSingleton<Lookups>();

        services.AddSingleton(sp => new AuthSession(Client(network, TimeSpan.FromSeconds(20)), secureStore,
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new ConnectionMonitor(Client(network, TimeSpan.FromSeconds(8)),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new ApiClient(
            Client(new AuthenticatedHttpHandler(sp.GetRequiredService<AuthSession>()) { InnerHandler = network },
                TimeSpan.FromSeconds(30)),
            sp.GetRequiredService<ConnectionMonitor>()));

        services.AddSingleton<MainWindowViewModel>();
        AddWithFactory<LoginViewModel>(services);
        AddWithFactory<ShellViewModel>(services);
        AddWithFactory<DashboardViewModel>(services);
        AddWithFactory<CalendarViewModel>(services);
        AddWithFactory<ProjectsViewModel>(services);
        AddWithFactory<ShowsViewModel>(services);
        AddWithFactory<ReleasesViewModel>(services);
        AddWithFactory<TasksViewModel>(services);
        AddWithFactory<TeamViewModel>(services);
        AddWithFactory<SettingsViewModel>(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void AddWithFactory<T>(IServiceCollection services) where T : class
    {
        services.AddTransient<T>();
        services.AddSingleton<Func<T>>(sp => sp.GetRequiredService<T>);
    }
}
