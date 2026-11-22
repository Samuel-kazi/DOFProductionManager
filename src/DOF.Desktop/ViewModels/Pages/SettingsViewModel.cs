using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Desktop.Services;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services.Api;

namespace DOF.Desktop.ViewModels.Pages;

public sealed partial class SettingsViewModel(
    AuthSession session,
    PreferencesStore preferences,
    IThemeService theme,
    ConnectionMonitor connection,
    DesktopSettings settings,
    ApiClient api) : PageViewModel
{
    public override string Title => "Settings";

    // ---- profile (name and timezone) and calendar default ----

    public IReadOnlyList<string> TimeZones { get; } = [.. TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.HasIanaId ? z.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : null)
        .OfType<string>().Distinct().Order()];

    [ObservableProperty] public partial string EditableName { get; set; } = session.CurrentUser?.FullName ?? string.Empty;
    [ObservableProperty] public partial string? SelectedTimeZone { get; set; } = session.CurrentUser?.TimeZone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileMessage))]
    public partial string? ProfileMessage { get; private set; }

    public bool HasProfileMessage => ProfileMessage is not null;

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        ProfileMessage = null;
        if (SelectedTimeZone is null)
        {
            return;
        }
        CurrentUserResponse? updated = null;
        if (await RunAsync(async () => updated = await api.PatchAsync<CurrentUserResponse>("api/v1/auth/me",
                new UpdateMyProfileRequest(EditableName, SelectedTimeZone))))
        {
            session.UpdateCurrentUser(updated!);
            ProfileMessage = "Saved. Times are now shown in " + updated!.TimeZone + ".";
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(TimeZone));
        }
    }

    public IReadOnlyList<string> CalendarViews { get; } = ["Month", "Week", "Day", "Agenda"];

    public string DefaultCalendarView
    {
        get => preferences.Current.DefaultCalendarView;
        set
        {
            if (value != preferences.Current.DefaultCalendarView && CalendarViews.Contains(value))
            {
                preferences.Update(p => p with { DefaultCalendarView = value });
                OnPropertyChanged();
            }
        }
    }

    // ---- distribution channels (settings.manage) ----

    public bool CanManageChannels => session.CurrentUser?.Permissions.Contains("settings.manage") == true;
    public ObservableCollection<DistributionChannelResponse> Channels { get; } = [];
    public IReadOnlyList<string> ChannelTypes => ReleaseValues.ChannelTypes;
    [ObservableProperty] public partial string NewChannelName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewChannelType { get; set; } = "Video";

    public override async Task LoadAsync()
    {
        if (!CanManageChannels)
        {
            return;
        }
        await RunAsync(async () =>
        {
            var channels = await api.GetAsync<List<DistributionChannelResponse>>("api/v1/distribution-channels?includeInactive=true");
            Channels.Clear();
            foreach (var channel in channels)
            {
                Channels.Add(channel);
            }
        });
    }

    [RelayCommand]
    private async Task AddChannelAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewChannelName)
            && await RunAsync(() => api.PostAsync<DistributionChannelResponse>("api/v1/distribution-channels",
                new DistributionChannelRequest(NewChannelName.Trim(), NewChannelType, null))))
        {
            NewChannelName = string.Empty;
            await LoadAsync();
        }
    }

    /// <summary>Channels are never deleted; deactivating hides them from new releases and keeps existing ones.</summary>
    [RelayCommand]
    private async Task ToggleChannelAsync(DistributionChannelResponse channel)
    {
        if (await RunAsync(() => api.PutAsync<DistributionChannelResponse>($"api/v1/distribution-channels/{channel.Id}",
                new DistributionChannelRequest(channel.Name, channel.ChannelType, channel.Description, !channel.IsActive))))
        {
            await LoadAsync();
        }
    }

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
