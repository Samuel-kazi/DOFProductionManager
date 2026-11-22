using System.Text.Json;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Reminders;

/// <summary>The outbox payload of an e-mail. <see cref="ReminderId"/> links it back to the reminder it delivers.</summary>
public sealed record EmailPayload(Guid UserId, string Subject, string Body, Guid? ReminderId);

/// <summary>Writes notifications and queued e-mails, each guarded by an idempotency key.</summary>
public sealed class NotificationWriter(IApplicationDbContext db, IOptions<OrganisationOptions> organisation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Adds a notification unless one with this key exists. Returns whether it was added.</summary>
    public async Task<bool> NotifyAsync(Guid userId, string title, string message, NotificationType type, ReminderSourceType? sourceType,
        Guid? sourceId, string key, CancellationToken ct)
    {
        if (db.Notifications.Local.Any(n => n.IdempotencyKey == key) || await db.Notifications.AnyAsync(n => n.IdempotencyKey == key, ct))
        {
            return false;
        }
        db.Notifications.Add(new Notification(userId, title, message, type, sourceType, sourceId, key));
        return true;
    }

    /// <summary>Queues an e-mail unless one with this key exists. Returns whether it was queued.</summary>
    public async Task<bool> QueueEmailAsync(EmailPayload payload, string key, DateTimeOffset now, CancellationToken ct)
    {
        if (db.OutboxMessages.Local.Any(m => m.IdempotencyKey == key) || await db.OutboxMessages.AnyAsync(m => m.IdempotencyKey == key, ct))
        {
            return false;
        }
        db.OutboxMessages.Add(new OutboxMessage(OutboxMessage.SendEmail, JsonSerializer.Serialize(payload, Json), key, now));
        return true;
    }

    public static EmailPayload ReadEmail(string payload) =>
        JsonSerializer.Deserialize<EmailPayload>(payload, Json) ?? throw new InvalidOperationException("Empty e-mail payload.");

    /// <summary>A time in the person's own timezone, e.g. "Tue 7 Mar 14:00".</summary>
    public async Task<string> LocalTimeAsync(Guid userId, DateTimeOffset instant, CancellationToken ct)
    {
        var zoneId = await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).SingleOrDefaultAsync(ct);
        var zone = ZonedTime.Find(zoneId ?? organisation.Value.DefaultTimeZone);
        return TimeZoneInfo.ConvertTime(instant, zone).ToString("ddd d MMM HH:mm");
    }
}
