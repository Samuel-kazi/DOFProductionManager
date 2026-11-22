using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Reminders;

namespace DOF.Desktop.ViewModels.Pages;

public sealed record ReminderViewOption(string Label, string[] Statuses);

/// <summary>Reminder centre (spec 6.6): upcoming, delivered, failed and cancelled reminders, and notification preferences.</summary>
public sealed partial class RemindersViewModel(ApiClient api, UserClock clock) : PageViewModel
{
    private int _loadGeneration;

    public override string Title => "Reminders";

    public IReadOnlyList<ReminderViewOption> Views { get; } =
    [
        new("Upcoming", ["Pending", "Processing"]), new("Delivered", ["Delivered"]), new("Failed", ["Failed"]),
        new("Cancelled", ["Cancelled"]),
    ];

    [ObservableProperty] public partial ReminderViewOption SelectedView { get; set; } = null!;

    partial void OnSelectedViewChanged(ReminderViewOption value) => _ = LoadAsync();

    public ObservableCollection<ReminderRowViewModel> Items { get; } = [];
    public bool IsEmpty => !IsLoading && !HasError && Items.Count == 0;

    // ---- preferences ----
    public IReadOnlyList<OffsetOption> Offsets { get; } = [.. ReminderValues.StandardOffsets.Select(m => new OffsetOption(m))];
    [ObservableProperty] public partial bool EmailEnabled { get; set; }
    [ObservableProperty] public partial bool EmailAvailable { get; private set; }
    [ObservableProperty] public partial bool DesktopEnabled { get; set; }
    [ObservableProperty] public partial bool OverdueAlerts { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreferencesMessage))]
    public partial string? PreferencesMessage { get; private set; }

    public bool HasPreferencesMessage => PreferencesMessage is not null;
    public string EmailHint => EmailAvailable ? "Also send reminders and alerts by e-mail." : "E-mail is not set up on the server yet.";

    /// <summary>Set by the shell so saved desktop pop-up preferences take effect immediately.</summary>
    public NotificationCenterViewModel? Notifications { get; set; }

    public override async Task LoadAsync()
    {
        SelectedView ??= Views[0];
        var generation = ++_loadGeneration;
        var path = "api/v1/reminders?pageSize=100" + string.Concat(SelectedView.Statuses.Select(s => $"&statuses={s}"));
        await RunAsync(async () =>
        {
            var page = await api.GetAsync<PagedResponse<ReminderResponse>>(path);
            var prefs = await api.GetAsync<NotificationPreferencesResponse>("api/v1/notification-preferences");
            if (generation != _loadGeneration)
            {
                return;
            }
            Items.Clear();
            foreach (var r in page.Items)
            {
                Items.Add(new ReminderRowViewModel(r, clock));
            }
            EmailAvailable = prefs.EmailAvailable;
            EmailEnabled = prefs.EmailEnabled;
            DesktopEnabled = prefs.DesktopEnabled;
            OverdueAlerts = prefs.OverdueAlerts;
            foreach (var option in Offsets)
            {
                option.IsSelected = prefs.ReminderOffsets.Contains(option.Minutes);
            }
            OnPropertyChanged(nameof(EmailHint));
        });
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        PreferencesMessage = null;
        if (await RunAsync(() => api.PutAsync<NotificationPreferencesResponse>("api/v1/notification-preferences",
                new UpdateNotificationPreferencesRequest(EmailEnabled, DesktopEnabled, OverdueAlerts,
                    [.. Offsets.Where(o => o.IsSelected).Select(o => o.Minutes)]))))
        {
            PreferencesMessage = "Saved. Your upcoming reminders are being updated.";
            if (Notifications is { } center)
            {
                center.DesktopPopups = DesktopEnabled;
            }
        }
    }

    [RelayCommand]
    private async Task CancelReminderAsync(ReminderRowViewModel row)
    {
        if (row.CanCancel && await RunAsync(() => api.DeleteAsync($"api/v1/reminders/{row.Id}")))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task OpenAsync(ReminderRowViewModel row) =>
        row.Reminder.SourceTitle is null ? Task.CompletedTask
            : Navigator?.OpenRecordAsync(row.Reminder.SourceType, row.Reminder.SourceId) ?? Task.CompletedTask;
}

/// <summary>Adds a personal reminder from a record's details panel.</summary>
public static class RemindMe
{
    public static async Task AddAsync(ApiClient api, RemindMeViewModel remind)
    {
        if (remind.Selected is not { } offset)
        {
            remind.Message = "Choose when to be reminded.";
            return;
        }
        try
        {
            await api.PostAsync<ReminderResponse>("api/v1/reminders",
                new CreateReminderRequest(remind.SourceType, remind.SourceId, offset.Minutes, null));
            remind.Message = $"You'll be reminded {offset.Label.ToLowerInvariant()}.";
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            remind.Message = ex.Message;
        }
    }
}
