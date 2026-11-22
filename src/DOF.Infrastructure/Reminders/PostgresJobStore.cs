using DOF.Application.Common.Abstractions;
using DOF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DOF.Infrastructure.Reminders;

/// <summary>
/// Claims work with a single UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) statement, so
/// concurrent workers (several API instances, or overlapping runs) never take the same item.
/// </summary>
public sealed class PostgresJobStore(DofDbContext db) : IJobStore
{
    /// <summary>Items left in "processing" this long (a crashed worker) are picked up again.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<Guid>> ClaimDueRemindersAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        var stale = now - StaleAfter;
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE reminders SET status = 'Processing', processing_started_at = {now}, updated_at = {now}
            WHERE id IN (
                SELECT id FROM reminders
                WHERE (status = 'Pending' AND scheduled_at <= {now})
                   OR (status = 'Processing' AND channel = 'InApp' AND processing_started_at < {stale})
                ORDER BY scheduled_at
                LIMIT {max}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ClaimOutboxAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        var stale = now - StaleAfter;
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE notification_outbox SET status = 'Processing', updated_at = {now}
            WHERE id IN (
                SELECT id FROM notification_outbox
                WHERE (status = 'Pending' AND next_attempt_at <= {now})
                   OR (status = 'Processing' AND updated_at < {stale})
                ORDER BY next_attempt_at
                LIMIT {max}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }

    public Task<int> PurgeProcessedOutboxAsync(DateTimeOffset before, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM notification_outbox WHERE status = 'Processed' AND processed_at < {before}", ct);

    public bool IsUniqueViolation(Exception exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
