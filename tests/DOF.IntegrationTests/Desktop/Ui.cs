using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DOF.Desktop.ViewModels;
using DOF.Desktop.Views;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Shared helpers for headless desktop UI tests.</summary>
internal static class Ui
{
    private static readonly string SnapshotDirectory = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");

    /// <summary>
    /// Runs the whole async test on the Avalonia UI thread and waits for it. The Func&lt;Task&lt;T&gt;&gt;
    /// overload is used deliberately: a plain Func&lt;Task&gt; binds to Dispatch&lt;Task&gt; and is not awaited.
    /// </summary>
    public static Task Run(HeadlessSession headless, Func<Task> test) => headless.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, Ct);

    public static MainWindow Show(MainWindowViewModel main)
    {
        var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
        window.Show();
        return window;
    }

    public static async Task<ShellViewModel> SignInAsync(
        MainWindowViewModel main, string email = DofApiFactory.AdminEmail, string password = DofApiFactory.AdminPassword)
    {
        await main.InitializeAsync();
        var login = Xunit.Assert.IsType<LoginViewModel>(main.Content);
        login.Email = email;
        login.Password = password;
        await login.SignInCommand.ExecuteAsync(null);
        var shell = await WaitForAsync(() => main.Content as ShellViewModel);
        // Let the first page finish loading so nothing is still in flight when a test disposes its services.
        await WaitForAsync(() => shell.CurrentPage is { IsLoading: false } page ? page : null);
        return shell;
    }

    public static async Task<T> WaitForAsync<T>(Func<T?> probe) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } value)
            {
                return value;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("The expected UI state was not reached within 10 seconds.");
    }

    public static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public static List<T> Find<T>(Window window) where T : Visual
    {
        Render(window);
        return window.GetVisualDescendants().OfType<T>().ToList();
    }

    /// <summary>Saves the current frame. A frame is only produced after something changed, so retry a few ticks.</summary>
    public static void Snapshot(Window window, string name)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Render(window);
            if (window.CaptureRenderedFrame() is { } frame)
            {
                frame.Save(Path.Combine(SnapshotDirectory, $"{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                return;
            }
            window.InvalidateVisual();
        }
        Xunit.Assert.Fail($"No frame was rendered for snapshot '{name}'.");
    }
}
