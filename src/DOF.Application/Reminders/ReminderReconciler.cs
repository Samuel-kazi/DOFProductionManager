using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Reminders;

/// <summary>
/// Job 3 (spec Part 7): brings a record's pending reminders in line with the record. Obsolete pending
/// reminders are cancelled (kept for history), future ones are (re)created at the new time, delivered and
/// failed ones are never touched. Safe to run any number of times.
/// </summary>
public sealed class ReminderReconciler(
    IApplicationDbContext db, ReminderSources sources, IEmailSender email, ILogger<ReminderReconciler> logger)
{
    public const string Rescheduled = "The record's time changed";
    public const string NoLongerApplies = "No longer applies";
    public const string Closed = "The record was closed, removed or has no time";

    public async Task ReconcileAsync(ReminderSourceType type, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var target = await sources.FindAsync(type, id, ct);
        var pending = await db.Reminders.Where(r => r.SourceType == type && r.SourceId == id && r.Status == ReminderStatus.Pending)
            .ToListAsync(ct);

        if (target is not { IsActive: true, Anchor: { } anchor })
        {
            foreach (var reminder in pending.Where(r => r.IsAutomatic || r.OffsetMinutes is not null || target is not { IsActive: true }))
            {
                reminder.Cancel(Closed);
            }
            await SaveAsync(type, id, ct);
            return;
        }

        var preferences = await PreferencesAsync(target.Recipients, ct);
        var desired = new List<(Guid User, int Offset, ReminderChannel Channel, DateTimeOffset At)>();
        foreach (var user in target.Recipients)
        {
            var pref = preferences.GetValueOrDefault(user);
            var offsets = pref?.ReminderOffsets ?? NotificationPreference.DefaultOffsets;
            var channels = pref is { EmailEnabled: true } && email.IsConfigured
                ? new[] { ReminderChannel.InApp, ReminderChannel.Email }
                : [ReminderChannel.InApp];
            foreach (var offset in offsets)
            {
                var at = anchor.ToUniversalTime().AddMinutes(-offset);
                if (at > now)
                {
                    desired.AddRange(channels.Select(c => (user, offset, c, at)));
                }
            }
        }

        // Automatic reminders: keep exact matches, cancel the rest, add what is missing.
        var automatic = pending.Where(r => r.IsAutomatic).ToList();
        foreach (var reminder in automatic)
        {
            var match = desired.Any(d => d.User == reminder.UserId && d.Offset == reminder.OffsetMinutes
                                         && d.Channel == reminder.Channel && d.At == reminder.ScheduledAt);
            if (!match)
            {
                var sameRule = desired.Any(d => d.User == reminder.UserId && d.Offset == reminder.OffsetMinutes && d.Channel == reminder.Channel);
                reminder.Cancel(sameRule ? Rescheduled : NoLongerApplies);
            }
        }
        foreach (var want in desired.Where(d => !automatic.Any(r => r.IsPending && r.UserId == d.User
                     && r.OffsetMinutes == d.Offset && r.Channel == d.Channel && r.ScheduledAt == d.At)))
        {
            db.Reminders.Add(Reminder.ForOffset(want.User, type, id, want.Offset, anchor, want.Channel, automatic: true));
        }

        // Personal reminders counted back from the record follow it; fixed-time ones stay.
        foreach (var reminder in pending.Where(r => !r.IsAutomatic && r.OffsetMinutes is not null))
        {
            var at = reminder.ScheduleFor(anchor);
            if (at != reminder.ScheduledAt)
            {
                reminder.Cancel(Rescheduled);
                if (at > now)
                {
                    db.Reminders.Add(Reminder.ForOffset(reminder.UserId, type, id, reminder.OffsetMinutes!.Value, anchor,
                        reminder.Channel, automatic: false));
                }
            }
        }

        await SaveAsync(type, id, ct);
    }

    private async Task<Dictionary<Guid, NotificationPreference>> PreferencesAsync(IReadOnlyList<Guid> users, CancellationToken ct) =>
        users.Count == 0
            ? []
            : await db.NotificationPreferences.AsNoTracking().Where(p => users.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);

    private async Task SaveAsync(ReminderSourceType type, Guid id, CancellationToken ct)
    {
        var changes = await db.SaveChangesAsync(ct);
        if (changes > 0)
        {
            logger.LogInformation("Reconciled reminders for {SourceType} {SourceId} ({Changes} changes)", type, id, changes);
        }
    }
}
