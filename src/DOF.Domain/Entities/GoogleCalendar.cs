using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// The organisation's Google account that sends calendar invites. Only one connection is active at a
/// time; a disconnected one is archived (kept for history). The refresh token is stored encrypted.
/// </summary>
public sealed class GoogleCalendarConnection : Entity, IArchivable
{
    public const string PrimaryCalendar = "primary";

    private GoogleCalendarConnection() { }

    public GoogleCalendarConnection(string accountEmail, string encryptedRefreshToken, Guid connectedBy)
    {
        AccountEmail = accountEmail.Trim().ToLowerInvariant();
        EncryptedRefreshToken = encryptedRefreshToken;
        CalendarId = PrimaryCalendar;
        ConnectedBy = connectedBy;
    }

    public string AccountEmail { get; private set; } = null!;
    public string EncryptedRefreshToken { get; private set; } = null!;
    public string CalendarId { get; private set; } = null!;
    public Guid ConnectedBy { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastErrorAt { get; private set; }

    public bool IsActive => ArchivedAt is null;

    public void Disconnect(DateTimeOffset now) => ArchivedAt ??= now;

    public void RecordError(string error, DateTimeOffset now)
    {
        LastError = error.Length > 1000 ? error[..1000] : error;
        LastErrorAt = now;
    }

    public void ClearError()
    {
        LastError = null;
        LastErrorAt = null;
    }
}

/// <summary>
/// Google Calendar invites for one standalone calendar event. The event stays the only owner of its
/// dates: the Google copy is rebuilt from it by the sync job whenever the event or the guest list
/// changes, and Google e-mails the guests each time. <see cref="GoogleEventId"/> is chosen here so a
/// retried insert can never create a second Google event.
/// </summary>
public sealed class GoogleCalendarInvite : AuditableEntity, IVersioned
{
    public const int MaxAttempts = 6;

    private readonly List<GoogleCalendarInviteGuest> _guests = [];

    private GoogleCalendarInvite() { }

    public GoogleCalendarInvite(Guid calendarEventId, Guid connectionId, DateTimeOffset now)
    {
        CalendarEventId = calendarEventId;
        ConnectionId = connectionId;
        GoogleEventId = $"dof{Id:N}";
        Status = GoogleInviteStatus.Pending;
        NextAttemptAt = now;
    }

    public Guid CalendarEventId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string GoogleEventId { get; private set; } = null!;
    public GoogleInviteStatus Status { get; private set; }
    /// <summary>Hash of what was last sent to Google; a different hash means the event changed since.</summary>
    public string? SyncedHash { get; private set; }
    /// <summary>Whether the Google event has been created at least once (later pushes are updates).</summary>
    public bool CreatedInGoogle { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public DateTimeOffset? ResponsesCheckedAt { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyCollection<GoogleCalendarInviteGuest> Guests => _guests;

    /// <summary>Replaces the guest list and queues a push (re-activating a removed invite).</summary>
    public void SetGuests(IEnumerable<(string Email, Guid? UserId, string? Name)> guests, Guid connectionId, DateTimeOffset now)
    {
        var wanted = guests
            .Select(g => (Email: g.Email.Trim().ToLowerInvariant(), g.UserId, g.Name))
            .GroupBy(g => g.Email)
            .ToDictionary(g => g.Key, g => g.First());
        if (wanted.Count == 0)
        {
            throw new DomainException("Choose at least one person to invite.");
        }

        _guests.RemoveAll(g => !wanted.ContainsKey(g.Email));
        foreach (var (email, guest) in wanted.Where(w => _guests.All(g => g.Email != w.Key)))
        {
            _guests.Add(new GoogleCalendarInviteGuest(Id, email, guest.UserId, guest.Name));
        }

        if (ConnectionId != connectionId)
        {
            // Sent again from a newly connected account: the old account's event cannot be updated.
            ConnectionId = connectionId;
            CreatedInGoogle = false;
            GoogleEventId = $"dof{Guid.CreateVersion7():N}";
        }
        Queue(GoogleInviteStatus.Pending, now);
    }

    /// <summary>Takes the event off the guests' calendars (the sync job deletes it from Google).</summary>
    public void RequestRemoval(DateTimeOffset now)
    {
        if (Status != GoogleInviteStatus.Removed)
        {
            Queue(GoogleInviteStatus.Removing, now);
        }
    }

    public void Synced(string hash, DateTimeOffset now)
    {
        Status = GoogleInviteStatus.Synced;
        SyncedHash = hash;
        CreatedInGoogle = true;
        LastSyncedAt = now;
        ResponsesCheckedAt = now;
        RetryCount = 0;
        LastError = null;
    }

    public void Removed(DateTimeOffset now, string? reason = null)
    {
        Status = GoogleInviteStatus.Removed;
        LastSyncedAt = now;
        RetryCount = 0;
        LastError = reason;
    }

    public void ResponsesChecked(DateTimeOffset now) => ResponsesCheckedAt = now;

    /// <summary>The same Google account was connected again; its existing event is still ours to update.</summary>
    public void MoveTo(Guid connectionId) => ConnectionId = connectionId;

    /// <summary>Waits for a precondition (such as a Google connection) without using up retries.</summary>
    public void Waiting(string reason, DateTimeOffset now, TimeSpan delay)
    {
        LastError = reason;
        NextAttemptAt = now + delay;
    }

    /// <summary>Records a failed push; retries with growing delays, then stops as <see cref="GoogleInviteStatus.Failed"/>.</summary>
    public void RecordFailure(string error, DateTimeOffset now)
    {
        RetryCount++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (RetryCount >= MaxAttempts)
        {
            Status = GoogleInviteStatus.Failed;
            return;
        }
        NextAttemptAt = now + OutboxMessage.RetryDelay(RetryCount);
    }

    /// <summary>Fails without retrying: the problem cannot fix itself.</summary>
    public void FailPermanently(string error)
    {
        Status = GoogleInviteStatus.Failed;
        LastError = error;
    }

    public void SetResponse(string email, GuestResponse response)
    {
        if (_guests.FirstOrDefault(g => g.Email == email.Trim().ToLowerInvariant()) is { } guest)
        {
            guest.Response = response;
        }
    }

    private void Queue(GoogleInviteStatus status, DateTimeOffset now)
    {
        Status = status;
        RetryCount = 0;
        LastError = null;
        NextAttemptAt = now;
    }
}

public sealed class GoogleCalendarInviteGuest : Entity
{
    private GoogleCalendarInviteGuest() { }

    internal GoogleCalendarInviteGuest(Guid inviteId, string email, Guid? userId, string? displayName)
    {
        InviteId = inviteId;
        Email = email;
        UserId = userId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        Response = GuestResponse.Pending;
    }

    public Guid InviteId { get; private set; }
    public string Email { get; private set; } = null!;
    /// <summary>The DOF user, when the guest is a team member; null for outside guests.</summary>
    public Guid? UserId { get; private set; }
    public string? DisplayName { get; private set; }
    public GuestResponse Response { get; internal set; }
}
