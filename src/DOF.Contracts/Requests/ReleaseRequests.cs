namespace DOF.Contracts.Requests;

/// <summary>
/// A release of one episode on one channel, or (without an episode) of content named by <see cref="Title"/>,
/// such as a single, trailer or announcement. Without a time it starts as a draft; <c>Scheduled</c> needs a time.
/// </summary>
public sealed record CreateReleaseRequest(
    Guid? EpisodeId,
    Guid DistributionChannelId,
    DateTimeOffset? ScheduledAt,
    string Status,
    Guid? AssignedTo,
    Guid? ProjectId = null,
    string? ExternalUrl = null,
    string? Notes = null,
    string? Title = null);

/// <summary>Details and workflow status (Draft, AwaitingApproval, Ready, Scheduled, Failed). Times change through /schedule or /postpone.</summary>
public sealed record UpdateReleaseRequest(
    string Status,
    Guid? AssignedTo,
    Guid? ProjectId,
    string? ExternalUrl,
    string? Notes,
    int Version);

public sealed record RescheduleReleaseRequest(DateTimeOffset ScheduledAt, int? Version = null);

/// <summary>
/// Confirms that the release actually went out. <see cref="Confirmed"/> must be true and
/// <see cref="ActualPublishedAt"/> is when it was published (not in the future). The scheduled time is kept.
/// </summary>
public sealed record PublishReleaseRequest(DateTimeOffset ActualPublishedAt, bool Confirmed, string? ExternalUrl = null, int? Version = null);

/// <summary>Postpones with a reason, to <see cref="NewScheduledAt"/> or until further notice. The previous time is kept in the history.</summary>
public sealed record PostponeReleaseRequest(string Reason, DateTimeOffset? NewScheduledAt = null, int? Version = null);

public sealed record CancelReleaseRequest(string Reason, int? Version = null);

public sealed record DistributionChannelRequest(string Name, string ChannelType, string? Description, bool IsActive = true);
