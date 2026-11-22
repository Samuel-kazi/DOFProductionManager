using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Shows;

namespace DOF.Desktop.ViewModels;

public sealed class NotificationItemViewModel(NotificationResponse notification, UserClock clock)
{
    public NotificationResponse Notification { get; } = notification;
    public string Title => Notification.Title;
    public string Message => Notification.Message;
    public bool IsUnread => !Notification.IsRead;
    public string WhenText => ShowLabels.When(Notification.CreatedAt, clock);
    public string TypeBrushKey => Notification.NotificationType switch
    {
        "Overdue" or "Escalation" => "Brush.Offline",
        _ => "Brush.Accent",
    };
    public bool CanOpen => Notification.SourceType is not null && Notification.SourceId is not null;
}

/// <summary>
/// The top-bar bell. Notifications are created by the server (reminders, overdue alerts, escalations);
/// the desktop polls for them while it is open and, when the person allows it, pops new ones up.
/// </summary>
public sealed partial class NotificationCenterViewModel(ApiClient api, UserClock clock) : ObservableObject
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly HashSet<Guid> _seen = [];
    private bool _primed;

    public ObservableCollection<NotificationItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread), nameof(BadgeText))]
    public partial int UnreadCount { get; private set; }

    public bool HasUnread => UnreadCount > 0;
    public string BadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Whether new notifications pop up on the desktop (the person's preference).</summary>
    public bool DesktopPopups { get; set; } = true;

    public IRecordNavigator? Navigator { get; set; }

    /// <summary>Raised for notifications that arrived since the last check (for desktop pop-ups).</summary>
    public event EventHandler<IReadOnlyList<NotificationResponse>>? Arrived;

    public async Task LoadPreferencesAsync()
    {
        try
        {
            DesktopPopups = (await api.GetAsync<NotificationPreferencesResponse>("api/v1/notification-preferences")).DesktopEnabled;
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            AppLog.Warning($"Could not load notification preferences: {ex.Message}");
        }
    }

    /// <summary>Fetches the latest notifications and the unread count. The first call only primes what has been seen.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var page = await api.GetAsync<PagedResponse<NotificationResponse>>("api/v1/notifications?pageSize=20");
            UnreadCount = (await api.GetAsync<UnreadCountResponse>("api/v1/notifications/unread-count")).Count;
            Items.Clear();
            foreach (var n in page.Items)
            {
                Items.Add(new NotificationItemViewModel(n, clock));
            }
            OnPropertyChanged(nameof(IsEmpty));

            var fresh = page.Items.Where(n => !n.IsRead && _seen.Add(n.Id)).ToList();
            foreach (var n in page.Items)
            {
                _seen.Add(n.Id);
            }
            if (_primed && fresh.Count > 0 && DesktopPopups)
            {
                Arrived?.Invoke(this, fresh);
            }
            _primed = true;
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            AppLog.Warning($"Could not refresh notifications: {ex.Message}");
        }
    }

    /// <summary>Refreshes now and then every <see cref="PollInterval"/> until cancelled (sign-out or window closed).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await LoadPreferencesAsync();
        await RefreshAsync();
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private async Task OpenAsync(NotificationItemViewModel item)
    {
        if (item.IsUnread)
        {
            await MarkReadAsync(item.Notification.Id);
        }
        if (item.CanOpen && Navigator is { } navigator)
        {
            await navigator.OpenRecordAsync(item.Notification.SourceType!, item.Notification.SourceId!.Value);
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await api.PatchAsync<UnreadCountResponse>("api/v1/notifications/read-all", new { });
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            AppLog.Warning($"Could not mark notifications read: {ex.Message}");
        }
        await RefreshAsync();
    }

    private async Task MarkReadAsync(Guid id)
    {
        try
        {
            await api.PatchNoContentAsync($"api/v1/notifications/{id}/read");
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            AppLog.Warning($"Could not mark notification read: {ex.Message}");
        }
    }
}
