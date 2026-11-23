namespace DOF.Contracts.Responses;

public sealed record DistributionChannelResponse(Guid Id, string Name, string ChannelType, string? Description, bool IsActive);

/// <summary>
/// A release. <see cref="Status"/> is the stored status; <see cref="DisplayStatus"/> is "Overdue" while
/// <see cref="IsOverdue"/> (open and past its time without publication confirmation), otherwise the status.
/// <see cref="ScheduledAt"/> is kept after publication alongside <see cref="ActualPublishedAt"/>.
/// <see cref="Episode"/> is null for content that is not an episode; <see cref="Title"/> is always the name to show.
/// </summary>
public sealed record ReleaseResponse(
    Guid Id,
    EpisodeRefResponse? Episode,
    ProjectRefResponse? Project,
    DistributionChannelResponse Channel,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ActualPublishedAt,
    string Status,
    string DisplayStatus,
    bool IsOverdue,
    UserSummaryResponse? AssignedTo,
    string? ExternalUrl,
    string? Notes,
    string? PostponementReason,
    string? CancellationReason,
    UserSummaryResponse? PublishedBy,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool CanEdit,
    bool CanConfirm,
    string Title);

/// <summary>
/// Content whose production is finished but whose distribution is not: open releases of approved, ready
/// or partially published episodes, and such episodes that have no release at all.
/// </summary>
public sealed record UnpublishedContentResponse(
    IReadOnlyList<ReleaseResponse> Releases,
    IReadOnlyList<EpisodeSummaryResponse> EpisodesWithoutReleases);
