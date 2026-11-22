using DOF.Domain.Enums;

namespace DOF.Domain.Entities;

/// <summary>
/// Work written in the same transaction as the change that causes it (spec 4.7 notification_outbox), so
/// it cannot be lost: e-mails to send and records whose reminders must be recalculated. A background
/// worker processes it with retries; <see cref="IdempotencyKey"/> is unique.
/// </summary>
public sealed class OutboxMessage : Entity
{
    public const string SendEmail = "SendEmail";
    public const string SourceChanged = "SourceChanged";

    private OutboxMessage() { }

    public OutboxMessage(string eventType, string payload, string idempotencyKey, DateTimeOffset now)
    {
        EventType = eventType;
        Payload = payload;
        IdempotencyKey = idempotencyKey;
        Status = OutboxStatus.Pending;
        NextAttemptAt = now;
    }

    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public OutboxStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    public void Processed(DateTimeOffset now)
    {
        Status = OutboxStatus.Processed;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Records a failed attempt; retries with growing delays until <paramref name="maxAttempts"/>, then fails.</summary>
    public bool RecordFailure(string error, DateTimeOffset now, int maxAttempts)
    {
        RetryCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (RetryCount >= maxAttempts)
        {
            Status = OutboxStatus.Failed;
            ProcessedAt = now;
            return true;
        }
        Status = OutboxStatus.Pending;
        NextAttemptAt = now + RetryDelay(RetryCount);
        return false;
    }

    /// <summary>1, 5, 15, 60 minutes, then 4 hours.</summary>
    public static TimeSpan RetryDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(4),
    };
}
