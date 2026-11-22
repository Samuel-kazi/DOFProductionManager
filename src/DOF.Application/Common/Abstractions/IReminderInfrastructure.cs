namespace DOF.Application.Common.Abstractions;

public sealed record EmailMessage(string To, string ToName, string Subject, string Body);

/// <summary>Sends e-mail. When the server has no mail settings, <see cref="IsConfigured"/> is false and e-mail cannot be chosen.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Database operations the background jobs need beyond EF Core: claiming work with row locks (so two
/// workers never take the same item) and housekeeping.
/// </summary>
public interface IJobStore
{
    /// <summary>Atomically marks due pending reminders (and stuck in-app ones) as processing and returns their ids.</summary>
    Task<IReadOnlyList<Guid>> ClaimDueRemindersAsync(DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>Atomically marks due outbox messages (and stuck ones) as processing and returns their ids.</summary>
    Task<IReadOnlyList<Guid>> ClaimOutboxAsync(DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>Deletes processed outbox messages older than <paramref name="before"/>. Returns the number removed.</summary>
    Task<int> PurgeProcessedOutboxAsync(DateTimeOffset before, CancellationToken ct);

    bool IsUniqueViolation(Exception exception);
}
