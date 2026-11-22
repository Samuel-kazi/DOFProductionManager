using Avalonia;
using Avalonia.Headless;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Security;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(DOF.IntegrationTests.Desktop.HeadlessSession))]

namespace DOF.IntegrationTests.Desktop;

internal sealed class InMemorySecureStore : ISecureStore
{
    public Dictionary<string, string> Items { get; } = [];
    public string? Get(string key) => Items.GetValueOrDefault(key);
    public void Set(string key, string value) => Items[key] = value;
    public void Remove(string key) => Items.Remove(key);
}

internal sealed class RecordingThemeService : IThemeService
{
    public ThemePreference? Applied { get; private set; }
    public void Apply(ThemePreference preference) => Applied = preference;
}

/// <summary>Builds the real desktop composition root, wired to the in-memory test API.</summary>
internal sealed class DesktopHarness : IDisposable
{
    private readonly string _preferencesDirectory =
        Path.Combine(Path.GetTempPath(), "dof-desktop-tests", Guid.NewGuid().ToString("N"));

    static DesktopHarness() => AppLog.Directory = Path.Combine(Path.GetTempPath(), "dof-desktop-tests", "logs");

    public DesktopHarness(DofApiFactory factory, InMemorySecureStore? store = null)
        : this(store, "http://localhost/", factory.Server.CreateHandler())
    {
    }

    /// <summary>A client whose server address has nothing listening, to simulate being offline.</summary>
    public static DesktopHarness Offline(InMemorySecureStore store) =>
        new(store, "http://127.0.0.1:9/", new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2) });

    private DesktopHarness(InMemorySecureStore? store, string apiUrl, HttpMessageHandler handler)
    {
        Store = store ?? new InMemorySecureStore();
        Preferences = new PreferencesStore(_preferencesDirectory);
        Services = DesktopServices.Build(DesktopSettings.FromUrl(apiUrl), Store, Preferences, Theme, handler);
    }

    public InMemorySecureStore Store { get; }
    public PreferencesStore Preferences { get; }
    public RecordingThemeService Theme { get; } = new();
    public ServiceProvider Services { get; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        Services.Dispose();
        if (Directory.Exists(_preferencesDirectory))
        {
            Directory.Delete(_preferencesDirectory, recursive: true);
        }
    }
}

/// <summary>
/// One headless Avalonia platform per test run, rendering with Skia so real views can be
/// laid out, exercised and captured as images.
/// </summary>
public sealed class HeadlessSession : IDisposable
{
    // Keep test runs out of the real user's desktop log folder.
    static HeadlessSession() => AppLog.Directory = Path.Combine(Path.GetTempPath(), "dof-desktop-tests", "logs");

    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessSession));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DOF.Desktop.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();

    public void Dispose() => Session.Dispose();
}
