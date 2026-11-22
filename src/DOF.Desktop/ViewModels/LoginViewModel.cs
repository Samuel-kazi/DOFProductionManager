using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AuthSession _session;
    private readonly PreferencesStore _preferences;

    public LoginViewModel(AuthSession session, PreferencesStore preferences, ConnectionMonitor connection)
    {
        _session = session;
        _preferences = preferences;
        Connection = connection;
        Email = preferences.Current.LastEmail ?? string.Empty;
    }

    public ConnectionMonitor Connection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    private bool CanSignIn() => !string.IsNullOrWhiteSpace(Email) && Password.Length > 0;

    /// <summary>On success the session announces the change and the main window switches to the shell.</summary>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        try
        {
            var result = await _session.LoginAsync(Email.Trim(), Password);
            if (result.Succeeded)
            {
                Connection.ReportSuccess();
                _preferences.Update(p => p with { LastEmail = Email.Trim() });
                Password = string.Empty;
                AppLog.Info("Signed in");
            }
            else
            {
                Connection.ReportSuccess();
                ErrorMessage = result.Error;
            }
        }
        catch (HttpRequestException ex)
        {
            Connection.ReportFailure();
            AppLog.Warning($"Sign-in failed, server unreachable: {ex.Message}");
            ErrorMessage = "Can't reach the DOF server. Check your connection and try again.";
        }
    }
}
