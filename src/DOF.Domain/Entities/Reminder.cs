using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// A server-side reminder for one person about one dated record. Automatic reminders come from the
/// person's default offsets and are recalculated whenever the record changes; personal ones are added
/// by the person (an offset before the record's time, or a fixed time).
/// </summary>
public sealed class Reminder : Entity, IVersioned
{
    /// <summary>The offsets offered in the UI, in minutes before the record's time (spec 4.7).</summary>
    public static readonly IReadOnlyList<int> StandardOffsets = [0, 15, 60, 1440, 4320, 10080];

    public const int MaxOffsetMinutes = 60 * 24 * 60;

    private Reminder() { }

    public Guid UserId { get; private set; }
    public ReminderSourceType SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    /// <summary>Minutes before the record's time; null for a fixed custom time.</summary>
    public int? OffsetMinutes { get; private set; }
    public bool IsAutomatic { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public ReminderChannel Channel { get; private set; }
    public ReminderStatus Status { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public int Version { get; private set; }

    public bool IsPending => Status == ReminderStatus.Pending;

    public static Reminder ForOffset(Guid userId, ReminderSourceType type, Guid sourceId, int offsetMinutes, DateTimeOffset anchor,
        ReminderChannel channel, bool automatic)
    {
        if (offsetMinutes < 0 || offsetMinutes > MaxOffsetMinutes)
        {
            throw new DomainException("A reminder must be between the record's time and 60 days before it.");
        }
        return new Reminder
        {
            UserId = userId, SourceType = type, SourceId = sourceId, OffsetMinutes = offsetMinutes, IsAutomatic = automatic,
            ScheduledAt = anchor.ToUniversalTime().AddMinutes(-offsetMinutes), Channel = channel, Status = ReminderStatus.Pending,
        };
    }

    public static Reminder AtTime(Guid userId, ReminderSourceType type, Guid sourceId, DateTimeOffset at, ReminderChannel channel) =>
        new()
        {
            UserId = userId, SourceType = type, SourceId = sourceId, ScheduledAt = at.ToUniversalTime(), Channel = channel,
            Status = ReminderStatus.Pending,
        };

    /// <summary>The time this reminder should fire for a record at <paramref name="anchor"/>.</summary>
    public DateTimeOffset ScheduleFor(DateTimeOffset anchor) =>
        OffsetMinutes is { } offset ? anchor.ToUniversalTime().AddMinutes(-offset) : ScheduledAt;

    public void Cancel(string reason)
    {
        if (Status is ReminderStatus.Pending or ReminderStatus.Processing)
        {
            Status = ReminderStatus.Cancelled;
            CancellationReason = reason;
        }
    }

    /// <summary>Changes a pending personal reminder (offset or fixed time, and channel).</summary>
    public void Change(int? offsetMinutes, DateTimeOffset? at, DateTimeOffset? anchor, ReminderChannel channel)
    {
        if (!IsPending)
        {
            throw new DomainException("Only pending reminders can be changed.");
        }
        if (offsetMinutes is { } offset)
        {
            if (anchor is not { } a)
            {
                throw new DomainException("This record has no date to count back from; choose a fixed time.");
            }
            if (offset < 0 || offset > MaxOffsetMinutes)
            {
                throw new DomainException("A reminder must be between the record's time and 60 days before it.");
            }
            OffsetMinutes = offset;
            ScheduledAt = a.ToUniversalTime().AddMinutes(-offset);
        }
        else if (at is { } fixedTime)
        {
            OffsetMinutes = null;
            ScheduledAt = fixedTime.ToUniversalTime();
        }
        else
        {
            throw new DomainException("Choose how long before, or a fixed time.");
        }
        Channel = channel;
    }

    public void Delivered(DateTimeOffset now)
    {
        Status = ReminderStatus.Delivered;
        DeliveredAt = now;
        LastError = null;
        ProcessingStartedAt = null;
    }

    /// <summary>Waiting for the outbox to send it (email).</summary>
    public void AwaitDelivery(DateTimeOffset now)
    {
        Status = ReminderStatus.Processing;
        ProcessingStartedAt = now;
    }

    public void RecordAttemptFailure(string error, bool final)
    {
        RetryCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (final)
        {
            Status = ReminderStatus.Failed;
            ProcessingStartedAt = null;
        }
    }
}
