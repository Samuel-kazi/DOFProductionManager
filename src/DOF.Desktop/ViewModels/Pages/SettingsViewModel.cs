using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels.Pages;

public sealed partial class SettingsViewModel(
    AuthSession session,
    PreferencesStore preferences,
    IThemeService theme,
    ConnectionMonitor connection,
    DesktopSettings settings) : PageViewModel
{
    public override string Title => "Settings";

    public IReadOnlyList<ThemePreference> ThemeOptions { get; } = Enum.GetValues<ThemePreference>();

    public string FullName => session.CurrentUser?.FullName ?? string.Empty;
    public string Email => session.CurrentUser?.Email ?? string.Empty;
    public string TimeZone => session.CurrentUser?.TimeZone ?? string.Empty;
    public string Roles => string.Join(", ", session.CurrentUser?.Roles ?? []);
    public string ServerAddress => settings.ApiBaseUrl.ToString();
    public string ServerVersion => connection.ServerVersion ?? "unknown";

    public ThemePreference SelectedTheme
    {
        get => preferences.Current.Theme;
        set
        {
            if (value == preferences.Current.Theme)
            {
                return;
            }

            preferences.Update(p => p with { Theme = value });
            theme.Apply(value);
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PasswordMessage { get; private set; }

    [ObservableProperty]
    public partial bool PasswordChanged { get; private set; }

    private bool CanChangePassword() =>
        CurrentPassword.Length > 0 && NewPassword.Length > 0 && ConfirmPassword.Length > 0;

    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    private async Task ChangePasswordAsync()
    {
        PasswordChanged = false;
        if (NewPassword != ConfirmPassword)
        {
            PasswordMessage = "The new passwords don't match.";
            return;
        }

        try
        {
            var result = await session.ChangePasswordAsync(CurrentPassword, NewPassword);
            PasswordChanged = result.Succeeded;
            PasswordMessage = result.Succeeded
                ? "Password changed. Other devices have been signed out."
                : result.Error;
            if (result.Succeeded)
            {
                CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
            }
        }
        catch (HttpRequestException)
        {
            connection.ReportFailure();
            PasswordMessage = "Can't reach the DOF server. Check your connection and try again.";
        }
    }
}
