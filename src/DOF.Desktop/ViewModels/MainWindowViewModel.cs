using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels;

/// <summary>
/// Decides what the window shows: a startup screen while restoring the session, the sign-in screen,
/// or the application shell. Also hosts the app-wide error banner.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthSession _session;
    private readonly ConnectionMonitor _connection;
    private readonly Func<LoginViewModel> _login;
    private readonly Func<ShellViewModel> _shell;

    public MainWindowViewModel(
        AuthSession session,
        ApiClient api,
        ConnectionMonitor connection,
        Func<LoginViewModel> login,
        Func<ShellViewModel> shell)
    {
        _session = session;
        _connection = connection;
        _login = login;
        _shell = shell;

        session.SessionChanged += async (_, _) => await ShowForSessionAsync();
        api.SessionExpired += async (_, _) =>
        {
            if (_session.IsSignedIn)
            {
                // The server rejected the session (revoked or deactivated): end it locally too.
                await _session.LogoutAsync();
            }
        };
    }

    [ObservableProperty]
    public partial ViewModelBase? Content { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppError))]
    public partial string? AppError { get; private set; }

    public bool HasAppError => AppError is not null;

    public bool IsStarting => Content is null;

    /// <summary>Restores a stored session if possible; otherwise shows the sign-in screen.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (await _session.TryRestoreAsync())
            {
                _connection.ReportSuccess();
                AppLog.Info("Session restored");
                return; // SessionChanged switches to the shell.
            }

            _connection.ReportSuccess();
        }
        catch (HttpRequestException ex)
        {
            // Server unreachable: keep the stored session for later and let the user retry from sign-in.
            _connection.ReportFailure();
            AppLog.Warning($"Could not restore session, server unreachable: {ex.Message}");
        }

        await ShowForSessionAsync();
    }

    public void ShowAppError(string message) => AppError = message;

    [RelayCommand]
    private void DismissAppError() => AppError = null;

    private async Task ShowForSessionAsync()
    {
        if (_session.IsSignedIn)
        {
            if (Content is ShellViewModel)
            {
                return; // e.g. tokens refreshed or password changed: stay where we are.
            }

            var shell = _shell();
            Content = shell;
            OnPropertyChanged(nameof(IsStarting));
            await shell.StartAsync();
        }
        else if (Content is not LoginViewModel)
        {
            Content = _login();
            OnPropertyChanged(nameof(IsStarting));
        }
    }
}
