using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// One episode, or a piece of content named by its <see cref="Title"/> (a single, trailer or announcement
/// that is not an episode), on one distribution channel, with its own schedule and status. A release is published
/// only by an explicit confirmation that records when it actually went out; the scheduled time is kept.
/// </summary>
public sealed class Release : AuditableEntity, IVersioned
{
    private Release() { }

    public const int TitleMaxLength = 200;

    /// <summary>The episode released, or null for content that is not an episode (then <see cref="Title"/> names it).</summary>
    public Guid? EpisodeId { get; private set; }

    /// <summary>What is released when there is no episode. Null for episode releases, which take the episode's title.</summary>
    public string? Title { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid DistributionChannelId { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? ActualPublishedAt { get; private set; }
    public ReleaseStatus Status { get; private set; }
    public Guid? AssignedTo { get; private set; }
    public string? ExternalUrl { get; private set; }
    public string? Notes { get; private set; }
    public string? PostponementReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public int Version { get; private set; }
    public Episode? Episode { get; private set; }
    public DistributionChannel Channel { get; private set; } = null!;

    /// <summary>Statuses that can be set through an ordinary edit. The others have their own actions.</summary>
    public static bool IsWorkflowStatus(ReleaseStatus status) => status is ReleaseStatus.Draft or ReleaseStatus.AwaitingApproval
        or ReleaseStatus.Ready or ReleaseStatus.Scheduled or ReleaseStatus.Failed;

    /// <summary>Still waiting to go out (and so able to become overdue).</summary>
    public static bool IsOpenStatus(ReleaseStatus status) => status is ReleaseStatus.Draft or ReleaseStatus.AwaitingApproval
        or ReleaseStatus.Ready or ReleaseStatus.Scheduled or ReleaseStatus.Postponed;

    public bool IsOpen => IsOpenStatus(Status);
    public bool IsClosed => Status is ReleaseStatus.Published or ReleaseStatus.Cancelled;

    /// <summary>Past its scheduled time without a publication confirmation. Never stored.</summary>
    public bool IsOverdue(DateTimeOffset now) => IsOpen && ScheduledAt is { } at && at < now;

    /// <summary>The name shown for the release: the episode's title, or the typed title.</summary>
    public string DisplayTitle => Episode?.Title ?? Title ?? string.Empty;

    /// <summary>Creates a release of an episode, or (with <paramref name="episode"/> null) of content named by <paramref name="title"/>.</summary>
    public static Release Create(Episode? episode, string? title, DistributionChannel channel, Guid? projectId, DateTimeOffset? scheduledAt,
        ReleaseStatus status, Guid? assignedTo, string? externalUrl, string? notes)
    {
        if (episode is not null)
        {
            if (episode.IsCancelled)
            {
                throw new DomainException("This episode is cancelled. Restore it before scheduling releases.");
            }
            episode.Show.EnsureNotArchived();
        }
        else if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Choose an episode or type what is being released.");
        }
        else if (title.Trim().Length > TitleMaxLength)
        {
            throw new DomainException($"The title can be at most {TitleMaxLength} characters.");
        }
        if (!channel.IsActive)
        {
            throw new DomainException("That channel is no longer active.");
        }

        var release = new Release
        {
            EpisodeId = episode?.Id, Episode = episode, Title = episode is null ? title!.Trim() : null,
            DistributionChannelId = channel.Id, Channel = channel,
        };
        release.ScheduledAt = scheduledAt?.ToUniversalTime();
        release.Update(projectId, status, assignedTo, externalUrl, notes);
        return release;
    }

    /// <summary>Edits the details and the workflow status of an open or failed release.</summary>
    public void Update(Guid? projectId, ReleaseStatus status, Guid? assignedTo, string? externalUrl, string? notes)
    {
        EnsureEditable();
        if (!IsWorkflowStatus(status))
        {
            throw new DomainException(status switch
            {
                ReleaseStatus.Published => "Publishing needs a confirmation with the actual publication time.",
                ReleaseStatus.Postponed => "Postponing needs a reason; use postpone.",
                ReleaseStatus.Cancelled => "Cancelling needs a reason; use cancel.",
                _ => "A release is overdue when its time passes without publication; it cannot be set as overdue.",
            });
        }
        if (status == ReleaseStatus.Scheduled && ScheduledAt is null)
        {
            throw new DomainException("Set a release time before marking it as scheduled.");
        }

        ProjectId = projectId;
        Status = status;
        AssignedTo = assignedTo;
        ExternalUrl = Clean(externalUrl);
        Notes = Clean(notes);
    }

    /// <summary>Moves the release to a new time. A postponed or failed release with a new time is scheduled again.</summary>
    public void Reschedule(DateTimeOffset scheduledAt)
    {
        EnsureEditable();
        ScheduledAt = scheduledAt.ToUniversalTime();
        if (Status is ReleaseStatus.Postponed or ReleaseStatus.Failed)
        {
            Status = ReleaseStatus.Scheduled;
        }
    }

    /// <summary>Postpones with a reason, to a new time or (without one) until further notice.</summary>
    public void Postpone(string reason, DateTimeOffset? newScheduledAt)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Say why the release is postponed.");
        }
        PostponementReason = reason.Trim();
        ScheduledAt = newScheduledAt?.ToUniversalTime();
        Status = ReleaseStatus.Postponed;
    }

    public void Cancel(string reason)
    {
        if (Status == ReleaseStatus.Published)
        {
            throw new DomainException("A published release cannot be cancelled.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Say why the release is cancelled.");
        }
        CancellationReason = reason.Trim();
        Status = ReleaseStatus.Cancelled;
    }

    /// <summary>
    /// Confirms publication at the time it actually happened (never in the future). The scheduled time is
    /// kept. Confirming again corrects the recorded time or link.
    /// </summary>
    public void ConfirmPublished(DateTimeOffset publishedAt, string? externalUrl, Guid? confirmedBy, DateTimeOffset now)
    {
        if (Status == ReleaseStatus.Cancelled)
        {
            throw new DomainException("A cancelled release cannot be published.");
        }
        if (publishedAt > now.AddMinutes(1))
        {
            throw new DomainException("The publication time cannot be in the future. Confirm once it has actually gone out.");
        }
        ActualPublishedAt = publishedAt.ToUniversalTime();
        ExternalUrl = Clean(externalUrl) ?? ExternalUrl;
        PublishedBy = confirmedBy;
        Status = ReleaseStatus.Published;
    }

    private void EnsureEditable()
    {
        if (IsClosed)
        {
            throw new DomainException(Status == ReleaseStatus.Published
                ? "This release is published. Its schedule and status are kept as they are."
                : "This release is cancelled.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
