using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.Services.Security;
using DOF.Desktop.ViewModels;
using DOF.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.Desktop;

public partial class App : Application
{
    private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromSeconds(15);
    private readonly CancellationTokenSource _shutdown = new();
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var preferences = new PreferencesStore();
            _services = DesktopServices.Build(
                DesktopSettings.Load(), SecureStoreFactory.CreateForCurrentPlatform(), preferences);
            _services.GetRequiredService<IThemeService>().Apply(preferences.Current.Theme);

            var main = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = main };

            // Unexpected errors are logged and shown to the user rather than crashing or vanishing.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.Error("Unhandled UI exception", e.Exception);
                main.ShowAppError($"Something went wrong: {e.Exception.Message}. Details were written to the log.");
                e.Handled = true;
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _shutdown.Cancel();
                _services.Dispose();
            };

            _ = RunGuardedAsync(() => _services.GetRequiredService<ConnectionMonitor>()
                .RunAsync(ConnectionCheckInterval, _shutdown.Token), main);
            _ = RunGuardedAsync(main.InitializeAsync, main);

            AppLog.Info($"Started; API {_services.GetRequiredService<DesktopSettings>().ApiBaseUrl}");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunGuardedAsync(Func<Task> work, MainWindowViewModel main)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error("Background operation failed", ex);
            main.ShowAppError($"Something went wrong: {ex.Message}. Details were written to the log.");
        }
    }
}
