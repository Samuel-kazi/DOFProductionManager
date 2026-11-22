using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.Reminders;

/// <summary>The signed-in person's reminders and notification preferences. Everything runs on the server.</summary>
public sealed class ReminderService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ReminderSources sources,
    IEmailSender email,
    IOptions<ReminderOptions> options,
    TimeProvider clock,
    ILogger<ReminderService> logger)
{
    private Guid Me => currentUser.RequiredUserId;

    public async Task<PagedResponse<ReminderResponse>> ListAsync(IReadOnlyCollection<string>? statuses, string? sourceType, Guid? sourceId,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var me = Me;
        var query = db.Reminders.AsNoTracking().Where(r => r.UserId == me);
        if (WorkParsing.ParseAll<ReminderStatus>(statuses) is { } wanted)
        {
            query = query.Where(r => wanted.Contains(r.Status));
        }
        if (sourceType is not null)
        {
            var type = WorkParsing.Parse<ReminderSourceType>(sourceType, "reminder source type");
            query = query.Where(r => r.SourceType == type);
        }
        if (sourceId is { } source)
        {
            query = query.Where(r => r.SourceId == source);
        }

        var total = await query.CountAsync(ct);
        // Upcoming first (soonest), then the rest newest first.
        var reminders = await query
            .OrderBy(r => r.Status != ReminderStatus.Pending)
            .ThenBy(r => r.Status == ReminderStatus.Pending ? r.ScheduledAt : DateTimeOffset.MaxValue)
            .ThenByDescending(r => r.ScheduledAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResponse<ReminderResponse>(await ToResponsesAsync(reminders, ct), p, size, total);
    }

    public async Task<ReminderResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        var reminder = await db.Reminders.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.UserId == me, ct)
                       ?? throw new NotFoundException("Reminder", id);
        return (await ToResponsesAsync([reminder], ct))[0];
    }

    public async Task<ReminderResponse> CreateAsync(CreateReminderRequest request, CancellationToken ct)
    {
        var type = WorkParsing.Parse<ReminderSourceType>(request.SourceType, "reminder source type");
        var channel = ParseChannel(request.Channel);
        var target = await sources.FindAsync(type, request.SourceId, ct)
                     ?? throw new DomainException("The record to be reminded about does not exist.");
        if (!target.IsActive && request.OffsetMinutes is not null)
        {
            throw new DomainException("This record is closed or has no time to count back from.");
        }

        var now = clock.GetUtcNow();
        var reminder = request.OffsetMinutes is { } offset
            ? Reminder.ForOffset(Me, type, request.SourceId, offset,
                target.Anchor ?? throw new DomainException("This record has no time to count back from; choose a fixed time."),
                channel, automatic: false)
            : Reminder.AtTime(Me, type, request.SourceId, request.At!.Value, channel);
        if (reminder.ScheduledAt <= now)
        {
            throw new DomainException("That reminder time has already passed.");
        }
        var me = Me;
        if (await db.Reminders.AnyAsync(r => r.UserId == me && r.SourceType == type && r.SourceId == request.SourceId
                                             && r.Status == ReminderStatus.Pending && r.Channel == channel
                                             && r.ScheduledAt == reminder.ScheduledAt, ct))
        {
            throw new ConflictException("You already have a reminder at that time for this record.");
        }

        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reminder {ReminderId} for {SourceType} {SourceId} created by {UserId}", reminder.Id, type, request.SourceId, Me);
        return await GetAsync(reminder.Id, ct);
    }

    public async Task<ReminderResponse> UpdateAsync(Guid id, UpdateReminderRequest request, CancellationToken ct)
    {
        var reminder = await LoadOwnAsync(id, ct);
        if (reminder.Version != request.Version)
        {
            throw new ConflictException("This reminder was changed elsewhere. Reload it and try again.");
        }
        db.Entry(reminder).Property(r => r.Version).OriginalValue = request.Version;
        if (reminder.IsAutomatic)
        {
            throw new DomainException("Automatic reminders follow your default reminder settings. Cancel it or change your defaults.");
        }
        var target = await sources.FindAsync(reminder.SourceType, reminder.SourceId, ct);
        reminder.Change(request.OffsetMinutes, request.At, target?.Anchor, ParseChannel(request.Channel));
        if (reminder.ScheduledAt <= clock.GetUtcNow())
        {
            throw new DomainException("That reminder time has already passed.");
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Cancels one of my pending reminders; it stays in the list as cancelled.</summary>
    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var reminder = await LoadOwnAsync(id, ct);
        if (reminder.Status is not ReminderStatus.Pending)
        {
            throw new DomainException("Only pending reminders can be cancelled.");
        }
        reminder.Cancel("Cancelled by you");
        await db.SaveChangesAsync(ct);
    }

    // ---------- preferences ----------

    public async Task<NotificationPreferencesResponse> GetPreferencesAsync(CancellationToken ct)
    {
        var me = Me;
        var pref = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == me, ct)
                   ?? new NotificationPreference(me);
        return Response(pref);
    }

    /// <summary>Saves preferences and recalculates the person's upcoming reminders with them.</summary>
    public async Task<NotificationPreferencesResponse> UpdatePreferencesAsync(UpdateNotificationPreferencesRequest request,
        CancellationToken ct)
    {
        if (request.EmailEnabled && !email.IsConfigured)
        {
            throw new DomainException("E-mail is not set up on the server yet, so it cannot be turned on.");
        }
        var me = Me;
        var pref = await db.NotificationPreferences.SingleOrDefaultAsync(p => p.UserId == me, ct);
        if (pref is null)
        {
            pref = new NotificationPreference(me);
            db.NotificationPreferences.Add(pref);
        }
        pref.Update(request.EmailEnabled, request.DesktopEnabled, request.OverdueAlerts, request.ReminderOffsets);

        // Queue every upcoming record that concerns me for reconciliation, in the same transaction.
        var now = clock.GetUtcNow();
        foreach (var (type, id) in await sources.UpcomingAsync(now, now.AddDays(options.Value.ReconcileHorizonDays), me, ct))
        {
            db.OutboxMessages.Add(new OutboxMessage(OutboxMessage.SourceChanged, OutboxProcessor.SourceChanged(type, id),
                $"source:{type}:{id}:{Guid.CreateVersion7()}", now));
        }
        await db.SaveChangesAsync(ct);
        return Response(pref);
    }

    private NotificationPreferencesResponse Response(NotificationPreference p) =>
        new(p.EmailEnabled, email.IsConfigured, p.DesktopEnabled, p.OverdueAlerts, p.ReminderOffsets);

    private async Task<Reminder> LoadOwnAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        return await db.Reminders.SingleOrDefaultAsync(r => r.Id == id && r.UserId == me, ct) ?? throw new NotFoundException("Reminder", id);
    }

    private ReminderChannel ParseChannel(string channel)
    {
        var parsed = WorkParsing.Parse<ReminderChannel>(channel, "reminder channel");
        if (parsed == ReminderChannel.Email && !email.IsConfigured)
        {
            throw new DomainException("E-mail is not set up on the server, so e-mail reminders are not available.");
        }
        return parsed;
    }

    private async Task<List<ReminderResponse>> ToResponsesAsync(IReadOnlyList<Reminder> reminders, CancellationToken ct)
    {
        var described = await sources.DescribeAsync(reminders.Select(r => (r.SourceType, r.SourceId)), ct);
        return reminders.Select(r =>
        {
            (string Title, DateTimeOffset? At)? target = described.TryGetValue((r.SourceType, r.SourceId), out var d) ? d : null;
            return new ReminderResponse(r.Id, r.SourceType.ToString(), r.SourceId, target?.Title, target?.At, r.OffsetMinutes,
                r.IsAutomatic, r.ScheduledAt, r.Channel.ToString(), r.Status.ToString(), r.DeliveredAt, r.RetryCount, r.LastError,
                r.CancellationReason, r.Version);
        }).ToList();
    }
}

/// <summary>The signed-in person's notifications.</summary>
public sealed class NotificationService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private Guid Me => currentUser.RequiredUserId;

    public async Task<PagedResponse<NotificationResponse>> ListAsync(bool unreadOnly, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var me = Me;
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == me && (!unreadOnly || !n.IsRead));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip((p - 1) * size).Take(size)
            .Select(n => new NotificationResponse(n.Id, n.Title, n.Message, n.NotificationType.ToString(),
                n.SourceType == null ? null : n.SourceType.ToString(), n.SourceId, n.IsRead, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);
        return new PagedResponse<NotificationResponse>(items, p, size, total);
    }

    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken ct)
    {
        var me = Me;
        return new UnreadCountResponse(await db.Notifications.CountAsync(n => n.UserId == me && !n.IsRead, ct));
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == me, ct)
                           ?? throw new NotFoundException("Notification", id);
        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public async Task<UnreadCountResponse> MarkAllReadAsync(CancellationToken ct)
    {
        var me = Me;
        var now = clock.GetUtcNow();
        foreach (var notification in await db.Notifications.Where(n => n.UserId == me && !n.IsRead).ToListAsync(ct))
        {
            notification.MarkRead(now);
        }
        await db.SaveChangesAsync(ct);
        return new UnreadCountResponse(0);
    }
}
