using System.Text.Json;
using DOF.Application.Activity;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Common;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.Reminders;

/// <summary>
/// Job 1 (spec Part 7): finds due reminders, claims them with a row lock, and delivers them: in-app
/// reminders become notifications, e-mail reminders are queued in the outbox. Runs on the server, so
/// reminders fire whether or not any desktop client is open.
/// </summary>
public sealed class ReminderDispatcher(
    IApplicationDbContext db, IJobStore store, ReminderSources sources, NotificationWriter writer,
    ILogger<ReminderDispatcher> logger)
{
    public const int BatchSize = 200;

    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await store.ClaimDueRemindersAsync(now, BatchSize, ct);
        var delivered = 0;
        foreach (var id in ids)
        {
            var reminder = await db.Reminders.SingleAsync(r => r.Id == id, ct);
            var target = await sources.FindAsync(reminder.SourceType, reminder.SourceId, ct);
            if (target is not { IsActive: true })
            {
                reminder.Cancel(ReminderReconciler.Closed);
                await db.SaveChangesAsync(ct);
                continue;
            }

            var when = target.Anchor is { } anchor ? await writer.LocalTimeAsync(reminder.UserId, anchor, ct) : null;
            var title = $"Reminder: {target.Title}";
            var message = (target.Kind, when) switch
            {
                (_, null) => $"{target.Kind} reminder.",
                ("Task", _) => $"Task due {when}.",
                ("Recording", _) => $"Recording starts {when}.",
                ("Release", _) => $"Release scheduled for {when}.",
                ("Milestone", _) => $"Milestone due {when}.",
                _ => $"Starts {when}.",
            };

            if (reminder.Channel == ReminderChannel.InApp)
            {
                await writer.NotifyAsync(reminder.UserId, title, message, NotificationType.Reminder, reminder.SourceType,
                    reminder.SourceId, $"reminder:{reminder.Id}", ct);
                reminder.Delivered(now);
            }
            else
            {
                await writer.QueueEmailAsync(new EmailPayload(reminder.UserId, title, message, reminder.Id),
                    $"email:reminder:{reminder.Id}", now, ct);
                reminder.AwaitDelivery(now);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                delivered++;
            }
            catch (DbUpdateException ex) when (store.IsUniqueViolation(ex))
            {
                // The unique idempotency key held: this reminder's notification or e-mail already exists.
                // Record the reminder as delivered rather than creating a second one.
                logger.LogWarning(ex, "Reminder {ReminderId} had already been delivered; marking it delivered", id);
                db.ClearTracked();
                var existing = await db.Reminders.SingleAsync(r => r.Id == id, ct);
                existing.Delivered(now);
                await db.SaveChangesAsync(ct);
            }
        }
        if (ids.Count > 0)
        {
            logger.LogInformation("Dispatched {Count} of {Claimed} due reminders", delivered, ids.Count);
        }
        return delivered;
    }
}

/// <summary>
/// Works through the outbox: recalculates reminders for changed records (job 3) and sends queued e-mails,
/// retrying failures with growing delays and recording the final failure on the message and its reminder.
/// </summary>
public sealed class OutboxProcessor(
    IApplicationDbContext db, IJobStore store, ReminderReconciler reconciler, IEmailSender email,
    IOptions<ReminderOptions> options, ILogger<OutboxProcessor> logger)
{
    public const int BatchSize = 200;

    public sealed record SourceChangedPayload(ReminderSourceType SourceType, Guid SourceId);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string SourceChanged(ReminderSourceType type, Guid id) => JsonSerializer.Serialize(new SourceChangedPayload(type, id), Json);

    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await store.ClaimOutboxAsync(now, BatchSize, ct);
        var reconciled = new HashSet<(ReminderSourceType, Guid)>();
        foreach (var id in ids)
        {
            var message = await db.OutboxMessages.SingleAsync(m => m.Id == id, ct);
            try
            {
                switch (message.EventType)
                {
                    case OutboxMessage.SourceChanged:
                        var source = JsonSerializer.Deserialize<SourceChangedPayload>(message.Payload, Json)!;
                        if (reconciled.Add((source.SourceType, source.SourceId)))
                        {
                            await reconciler.ReconcileAsync(source.SourceType, source.SourceId, now, ct);
                        }
                        break;
                    case OutboxMessage.SendEmail:
                        await SendAsync(message, now, ct);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown outbox event type '{message.EventType}'.");
                }
                message.Processed(now);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordFailureAsync(message.Id, ex, now, ct);
            }
        }
        return ids.Count;
    }

    private async Task SendAsync(OutboxMessage message, DateTimeOffset now, CancellationToken ct)
    {
        var payload = NotificationWriter.ReadEmail(message.Payload);
        if (!email.IsConfigured)
        {
            throw new InvalidOperationException("E-mail is not configured on the server.");
        }
        var user = await db.Users.AsNoTracking().Where(u => u.Id == payload.UserId).Select(u => new { u.Email, u.FullName, u.IsActive })
                       .SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("The recipient no longer exists.");
        if (!user.IsActive)
        {
            throw new InvalidOperationException("The recipient's account is deactivated.");
        }

        await email.SendAsync(new EmailMessage(user.Email, user.FullName, payload.Subject, payload.Body), ct);
        if (payload.ReminderId is { } reminderId && await db.Reminders.SingleOrDefaultAsync(r => r.Id == reminderId, ct) is { } reminder)
        {
            reminder.Delivered(now);
        }
    }

    /// <summary>Failures are always recorded (never swallowed): on the message, on its reminder, and in the log.</summary>
    private async Task RecordFailureAsync(Guid messageId, Exception ex, DateTimeOffset now, CancellationToken ct)
    {
        db.ClearTracked();
        var message = await db.OutboxMessages.SingleAsync(m => m.Id == messageId, ct);
        var final = message.RecordFailure(ex.Message, now, options.Value.MaxDeliveryAttempts);
        if (message.EventType == OutboxMessage.SendEmail
            && NotificationWriter.ReadEmail(message.Payload).ReminderId is { } reminderId
            && await db.Reminders.SingleOrDefaultAsync(r => r.Id == reminderId, ct) is { } reminder)
        {
            reminder.RecordAttemptFailure(ex.Message, final);
        }
        await db.SaveChangesAsync(ct);
        if (final)
        {
            logger.LogError(ex, "Outbox message {MessageId} ({EventType}) failed permanently after {Attempts} attempts",
                messageId, message.EventType, message.RetryCount);
        }
        else
        {
            logger.LogWarning(ex, "Outbox message {MessageId} ({EventType}) failed (attempt {Attempt}); retrying at {NextAttempt}",
                messageId, message.EventType, message.RetryCount, message.NextAttemptAt);
        }
    }
}

/// <summary>
/// Job 2 (spec Part 7): tells people about their overdue tasks, milestones and releases — once per item
/// and due time, however often it runs (idempotency keys include the due time, so rescheduling and
/// missing the new time alerts again).
/// </summary>
public sealed class OverdueDetector(
    IApplicationDbContext db, ReminderSources sources, NotificationWriter writer, IEmailSender email,
    ILogger<OverdueDetector> logger)
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var alerts = new List<(Guid User, string Title, string Message, ReminderSourceType Type, Guid Id, string Key)>();

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DueAt < now && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
            .Select(t => new { t.Id, t.Title, t.DueAt, Users = t.Assignments.Select(a => a.UserId).ToList() }).ToListAsync(ct);
        foreach (var t in tasks)
        {
            alerts.AddRange(t.Users.Select(u => (u, $"Overdue: {t.Title}", "This task is past its due time.", ReminderSourceType.Task, t.Id,
                $"overdue:task:{t.Id}:{t.DueAt!.Value.UtcTicks}:{u}")));
        }

        var today = sources.Today(now);
        var milestones = await db.ProjectMilestones.AsNoTracking()
            .Where(m => m.AssignedTo != null && m.DueDate < today && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled
                        && m.Project.Status != ProjectStatus.Archived)
            .Select(m => new { m.Id, m.Title, m.DueDate, User = m.AssignedTo!.Value }).ToListAsync(ct);
        foreach (var m in milestones)
        {
            alerts.Add((m.User, $"Overdue: {m.Title}", "This milestone is past its due date.", ReminderSourceType.Milestone, m.Id,
                $"overdue:milestone:{m.Id}:{m.DueDate:yyyyMMdd}:{m.User}"));
        }

        var releases = await db.Releases.AsNoTracking()
            .Where(r => r.ScheduledAt < now && (r.Status == ReleaseStatus.Draft || r.Status == ReleaseStatus.AwaitingApproval
                                                || r.Status == ReleaseStatus.Ready || r.Status == ReleaseStatus.Scheduled
                                                || r.Status == ReleaseStatus.Postponed))
            .Select(r => new { r.Id, r.ScheduledAt, Channel = r.Channel.Name, r.Episode.Title, User = r.AssignedTo ?? r.Episode.ProducerId })
            .ToListAsync(ct);
        foreach (var r in releases.Where(r => r.User is not null))
        {
            alerts.Add((r.User!.Value, $"Missed release: {r.Channel} · {r.Title}",
                "This release is past its time and has not been confirmed as published.", ReminderSourceType.Release, r.Id,
                $"overdue:release:{r.Id}:{r.ScheduledAt!.Value.UtcTicks}:{r.User}"));
        }

        if (alerts.Count == 0)
        {
            return 0;
        }

        var keys = alerts.Select(a => a.Key).ToList();
        var existing = (await db.Notifications.AsNoTracking().Where(n => keys.Contains(n.IdempotencyKey)).Select(n => n.IdempotencyKey)
            .ToListAsync(ct)).ToHashSet();
        var fresh = alerts.Where(a => !existing.Contains(a.Key)).ToList();
        var userIds = fresh.Select(a => a.User).Distinct().ToList();
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, ct);
        var active = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct))
            .ToHashSet();

        var sent = 0;
        foreach (var alert in fresh.Where(a => active.Contains(a.User)))
        {
            var pref = preferences.GetValueOrDefault(alert.User);
            if (pref is { OverdueAlerts: false })
            {
                continue;
            }
            await writer.NotifyAsync(alert.User, alert.Title, alert.Message, NotificationType.Overdue, alert.Type, alert.Id, alert.Key, ct);
            if (pref is { EmailEnabled: true } && email.IsConfigured)
            {
                await writer.QueueEmailAsync(new EmailPayload(alert.User, alert.Title, alert.Message, null), $"email:{alert.Key}", now, ct);
            }
            sent++;
        }
        await db.SaveChangesAsync(ct);
        if (sent > 0)
        {
            logger.LogInformation("Sent {Count} overdue alerts", sent);
        }
        return sent;
    }
}

/// <summary>
/// Job 4 (spec Part 7): tasks overdue longer than the configured period are escalated to production
/// managers (administrators when there are none), once per task and due time, and recorded in the
/// task's history.
/// </summary>
public sealed class EscalationJob(
    IApplicationDbContext db, NotificationWriter writer, ActivityRecorder activity, IOptions<ReminderOptions> options,
    ILogger<EscalationJob> logger)
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-options.Value.EscalationAfterHours);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DueAt < cutoff && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
            .Select(t => new
            {
                t.Id, t.Title, t.DueAt,
                Owners = t.Assignments.OrderByDescending(a => a.IsPrimary).Select(a => a.User.FullName).ToList(),
            })
            .ToListAsync(ct);
        if (tasks.Count == 0)
        {
            return 0;
        }

        var managers = await ManagersAsync(SystemRoles.ProductionManager, ct);
        if (managers.Count == 0)
        {
            managers = await ManagersAsync(SystemRoles.Administrator, ct);
        }

        var escalated = 0;
        foreach (var task in tasks)
        {
            var notified = new List<string>();
            foreach (var manager in managers)
            {
                var key = $"escalation:task:{task.Id}:{task.DueAt!.Value.UtcTicks}:{manager.Id}";
                var owners = task.Owners.Count == 0 ? "nobody" : string.Join(", ", task.Owners);
                if (await writer.NotifyAsync(manager.Id, $"Escalated: {task.Title}",
                        $"Overdue for more than {options.Value.EscalationAfterHours} hours. Responsible: {owners}.",
                        NotificationType.Escalation, ReminderSourceType.Task, task.Id, key, ct))
                {
                    notified.Add(manager.FullName);
                }
            }
            if (notified.Count > 0)
            {
                activity.Record(ActivityEntities.Task, task.Id, ActivityActions.Escalated, null,
                    new { Notified = notified, OverdueSince = task.DueAt });
                escalated++;
            }
        }
        await db.SaveChangesAsync(ct);
        if (escalated > 0)
        {
            logger.LogInformation("Escalated {Count} overdue tasks to {Managers} managers", escalated, managers.Count);
        }
        return escalated;
    }

    private async Task<List<(Guid Id, string FullName)>> ManagersAsync(string role, CancellationToken ct) =>
        (await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Roles.Any(r => r.Role.Name == role))
            .Select(u => new { u.Id, u.FullName }).ToListAsync(ct))
        .Select(u => (u.Id, u.FullName)).ToList();
}

/// <summary>
/// Job 5: housekeeping. Removes old processed outbox messages, and re-reconciles every upcoming record so
/// reminders stay right even if a change was missed (for example new default offsets).
/// </summary>
public sealed class MaintenanceJob(
    IJobStore store, ReminderSources sources, ReminderReconciler reconciler, IOptions<ReminderOptions> options,
    ILogger<MaintenanceJob> logger)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var purged = await store.PurgeProcessedOutboxAsync(now.AddDays(-options.Value.OutboxRetentionDays), ct);
        var upcoming = await sources.UpcomingAsync(now, now.AddDays(options.Value.ReconcileHorizonDays), null, ct);
        foreach (var (type, id) in upcoming)
        {
            await reconciler.ReconcileAsync(type, id, now, ct);
        }
        logger.LogInformation("Maintenance: purged {Purged} outbox messages, reconciled {Count} upcoming records", purged, upcoming.Count);
    }
}
