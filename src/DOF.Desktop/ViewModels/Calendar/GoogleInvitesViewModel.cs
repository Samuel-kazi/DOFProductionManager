using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Desktop.Services;
using DOF.Desktop.Services.Api;
using DOF.Desktop.ViewModels.Work;

namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>A guest on the event's Google Calendar invite and their answer.</summary>
public sealed record GuestItem(string Name, string Email, string Response)
{
    public string ResponseText => Response switch
    {
        "Accepted" => "Going",
        "Tentative" => "Maybe",
        "Declined" => "Not going",
        _ => "No reply yet",
    };

    public string ResponseBrushKey => Response switch
    {
        "Accepted" => "Brush.Online",
        "Declined" => "Brush.Offline",
        _ => "Brush.TextMuted",
    };
}

/// <summary>
/// The "Google Calendar" section of an event: who has been invited, their answers, and sending or
/// removing invites. The server sends the invites and keeps them in step with the event.
/// </summary>
public sealed partial class GoogleInvitesViewModel(
    ApiClient api, UserClock clock, CalendarEventResponse calendarEvent, bool canManage) : ObservableObject
{
    private string Path => $"api/v1/calendar/events/{calendarEvent.Id}/google-invites";

    public bool CanManage { get; } = canManage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText), nameof(HasInvite), nameof(IsActive), nameof(CanSend), nameof(CanEditGuests),
        nameof(CanRemove), nameof(ProblemText), nameof(HasProblem), nameof(SendButtonText))]
    public partial GoogleInviteResponse? Invite { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanEditGuests), nameof(ShowConnectHint))]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanEditGuests), nameof(CanRemove))]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanEditGuests), nameof(CanRemove))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    [ObservableProperty] public partial bool IsConfirmingRemove { get; set; }

    [ObservableProperty] public partial string ExtraEmails { get; set; } = string.Empty;

    public ObservableCollection<GuestItem> Guests { get; } = [];
    public ObservableCollection<PersonOption> People { get; } = [];

    public bool HasMessage => Message is not null;
    public bool HasInvite => Invite is not null;
    public bool IsActive => Invite is { Status: not ("Removed" or "Removing") };
    private bool EventIsOpen => calendarEvent.Status != CalendarValues.Cancelled && calendarEvent.EndAt > DateTimeOffset.UtcNow;

    public bool ShowConnectHint => CanManage && !IsConnected && EventIsOpen;
    public bool CanSend => CanManage && IsConnected && EventIsOpen && !IsActive && !IsEditing && !IsBusy;
    public bool CanEditGuests => CanManage && IsConnected && EventIsOpen && IsActive && !IsEditing && !IsBusy;
    public bool CanRemove => CanManage && IsActive && !IsEditing && !IsBusy;
    public string SendButtonText => Invite?.Status == "Removed" ? "Send Google invites again…" : "Send Google invites…";

    public string? ProblemText => Invite is { Status: "Failed" or "Pending" or "Removing", LastError: { } error } ? error : null;
    public bool HasProblem => ProblemText is not null;

    public string SummaryText
    {
        get
        {
            if (Invite is not { } invite)
            {
                return "Not sent to Google Calendar.";
            }
            var people = invite.Guests.Count == 1 ? "1 person" : $"{invite.Guests.Count} people";
            var updated = invite.LastSyncedAt is { } at ? $" · updated {clock.ToLocal(at):d MMM HH:mm}" : string.Empty;
            return invite.Status switch
            {
                "Synced" => $"On {people}'s Google Calendar{updated}",
                "Pending" => $"Sending to {people}…",
                "Removing" => "Removing from guests' Google Calendars…",
                "Removed" => $"Removed from guests' Google Calendars{updated}",
                _ => "Google Calendar could not be updated.",
            };
        }
    }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var status = await api.GetAsync<GoogleCalendarStatusResponse>("api/v1/integrations/google-calendar");
            IsConnected = status.Connected;
            Show(await api.GetOptionalAsync<GoogleInviteResponse>(Path));
        });
    }

    [RelayCommand]
    private async Task StartEditingAsync()
    {
        Message = null;
        await RunAsync(async () =>
        {
            var users = await api.GetAsync<PagedResponse<UserResponse>>("api/v1/users?pageSize=100&isActive=true");
            var invitedIds = Invite is { Status: not "Removed" } invite
                ? invite.Guests.Select(g => g.UserId).OfType<Guid>().ToHashSet()
                : calendarEvent.Assignments.Select(a => a.UserId).ToHashSet();
            People.Clear();
            foreach (var user in users.Items.OrderBy(u => u.FullName, StringComparer.CurrentCultureIgnoreCase))
            {
                People.Add(new PersonOption(user.Id, user.FullName) { IsSelected = invitedIds.Contains(user.Id) });
            }
            ExtraEmails = Invite is { Status: not "Removed" } current
                ? string.Join(", ", current.Guests.Where(g => g.UserId is null).Select(g => g.Email))
                : string.Empty;
            IsEditing = true;
        });
    }

    [RelayCommand]
    private void CancelEditing()
    {
        IsEditing = false;
        Message = null;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var emails = ExtraEmails.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var userIds = People.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        if (userIds.Count == 0 && emails.Length == 0)
        {
            Message = "Choose at least one person to invite.";
            return;
        }
        if (await RunAsync(async () => Show(await api.PutAsync<GoogleInviteResponse>(Path, new SendGoogleInvitesRequest(userIds, emails)))))
        {
            IsEditing = false;
            Message = Invite?.Status == "Synced"
                ? "Invites sent. Google has e-mailed everyone, and the event will update on their calendars when you change it here."
                : "Saved. DOF will keep trying to reach Google Calendar.";
            AppLog.Info($"Google invites sent for event {calendarEvent.Id}");
        }
    }

    [RelayCommand]
    private void AskRemove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void KeepInvites() => IsConfirmingRemove = false;

    [RelayCommand]
    private async Task RemoveAsync()
    {
        IsConfirmingRemove = false;
        if (await RunAsync(async () => Show(await api.DeleteAsync<GoogleInviteResponse>(Path))))
        {
            Message = "The event has been taken off everyone's Google Calendar. Google has let them know.";
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private void Show(GoogleInviteResponse? invite)
    {
        Invite = invite;
        Guests.Clear();
        foreach (var g in invite?.Guests ?? [])
        {
            Guests.Add(new GuestItem(g.DisplayName ?? g.Email, g.Email, g.Response));
        }
    }

    private async Task<bool> RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex) when (ex is ApiException or ApiUnavailableException)
        {
            Message = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
