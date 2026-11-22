using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// One episode of a show, optionally within a season (one-off productions have none). Owns its recording
/// time and production deadlines; the calendar reads them from here and never copies them.
/// </summary>
public sealed class Episode : AuditableEntity, IVersioned, IArchivable
{
    private readonly List<EpisodeAssignment> _assignments = [];

    private Episode() { }

    public Guid ShowId { get; private set; }
    public Guid? SeasonId { get; private set; }
    public int? EpisodeNumber { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? ProducerId { get; private set; }
    public DateTimeOffset? RecordingStart { get; private set; }
    public DateTimeOffset? RecordingEnd { get; private set; }
    public string? RecordingLocation { get; private set; }
    public DateTimeOffset? RecordedAt { get; private set; }
    public DateOnly? EditingDueDate { get; private set; }
    public DateOnly? ReviewDueDate { get; private set; }
    public DateOnly? ApprovalDueDate { get; private set; }
    public EpisodeStatus Status { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public int Version { get; private set; }
    public Show Show { get; private set; } = null!;
    public Season? Season { get; private set; }

    public IReadOnlyCollection<EpisodeAssignment> Assignments => _assignments;

    public bool IsCancelled => Status == EpisodeStatus.Cancelled;

    /// <summary>"S2 E5", "E5" (no season) or "" (unnumbered one-off).</summary>
    public static string Code(int? seasonNumber, int? episodeNumber) => (seasonNumber, episodeNumber) switch
    {
        ({ } s, { } e) => $"S{s} E{e}",
        (null, { } e) => $"E{e}",
        ({ } s, null) => $"S{s}",
        _ => "",
    };

    /// <summary>Statuses that mean the recording has happened.</summary>
    public static bool IsAfterRecording(EpisodeStatus status) =>
        status is not (EpisodeStatus.Planned or EpisodeStatus.Scheduled or EpisodeStatus.Cancelled);

    /// <summary>Publication statuses follow release confirmations; they cannot be set by hand.</summary>
    public static bool IsPublicationStatus(EpisodeStatus status) =>
        status is EpisodeStatus.PartiallyPublished or EpisodeStatus.Published;

    public static Episode Create(Show show, Season? season, int? episodeNumber, string title, string? description, Guid? producerId)
    {
        show.EnsureNotArchived();
        var episode = new Episode { ShowId = show.Id, Status = EpisodeStatus.Planned };
        episode.Place(show, season, episodeNumber);
        episode.Describe(title, description, producerId);
        return episode;
    }

    /// <summary>Moves the episode within its show (a season of the same show, or none) and renumbers it.</summary>
    public void Place(Show show, Season? season, int? episodeNumber)
    {
        if (show.Id != ShowId)
        {
            throw new DomainException("An episode cannot be moved to a different show.");
        }
        if (season is not null && season.ShowId != ShowId)
        {
            throw new DomainException("That season belongs to a different show.");
        }
        if (episodeNumber is < 1)
        {
            throw new DomainException("Episode numbers start at 1.");
        }

        SeasonId = season?.Id;
        Season = season;
        EpisodeNumber = episodeNumber;
    }

    public void Describe(string title, string? description, Guid? producerId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("An episode needs a title.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ProducerId = producerId;
    }

    /// <summary>
    /// Sets (or clears) the recording session. With only a start, the end is the start plus the show's
    /// default duration, or one hour.
    /// </summary>
    public void ScheduleRecording(DateTimeOffset? start, DateTimeOffset? end, string? location, int? defaultDurationMinutes)
    {
        if (start is null)
        {
            if (end is not null)
            {
                throw new DomainException("A recording end needs a recording start.");
            }
            RecordingStart = null;
            RecordingEnd = null;
        }
        else
        {
            var finish = end ?? start.Value.AddMinutes(defaultDurationMinutes ?? 60);
            if (finish <= start)
            {
                throw new DomainException("The recording must end after it starts.");
            }
            RecordingStart = start.Value.ToUniversalTime();
            RecordingEnd = finish.ToUniversalTime();
        }

        RecordingLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
    }

    public void SetDeadlines(DateOnly? editing, DateOnly? review, DateOnly? approval)
    {
        if (editing is not null && review is not null && review < editing)
        {
            throw new DomainException("The review deadline cannot be before the editing deadline.");
        }
        if (review is not null && approval is not null && approval < review)
        {
            throw new DomainException("The approval deadline cannot be before the review deadline.");
        }
        if (editing is not null && approval is not null && approval < editing)
        {
            throw new DomainException("The approval deadline cannot be before the editing deadline.");
        }

        EditingDueDate = editing;
        ReviewDueDate = review;
        ApprovalDueDate = approval;
    }

    /// <summary>
    /// Sets any status (corrections included). Entering a post-recording stage records when the episode
    /// was recorded (the given time, else the end of a past recording session, else now); moving back to
    /// Planned or Scheduled clears it. Cancelling keeps it.
    /// </summary>
    public void ChangeStatus(EpisodeStatus status, DateTimeOffset now, DateTimeOffset? recordedAt = null)
    {
        if (status == EpisodeStatus.Scheduled && RecordingStart is null)
        {
            throw new DomainException("Schedule a recording time before marking the episode as scheduled.");
        }
        if (recordedAt is { } at && at > now)
        {
            throw new DomainException("The recording time cannot be in the future.");
        }

        if (IsAfterRecording(status))
        {
            RecordedAt = recordedAt?.ToUniversalTime() ?? RecordedAt ?? (RecordingEnd is { } end && end <= now ? end : now);
        }
        else if (status != EpisodeStatus.Cancelled)
        {
            RecordedAt = null;
        }

        ArchivedAt = status == EpisodeStatus.Cancelled ? ArchivedAt ?? now : null;
        Status = status;
    }

    /// <summary>Adds a responsibility. Returns false if the person already holds it.</summary>
    public bool Assign(Guid userId, EpisodeResponsibility responsibility, DateTimeOffset now)
    {
        if (_assignments.Any(a => a.UserId == userId && a.Responsibility == responsibility))
        {
            return false;
        }
        _assignments.Add(new EpisodeAssignment(Id, userId, responsibility, now));
        return true;
    }

    public EpisodeAssignment Unassign(Guid assignmentId)
    {
        var assignment = _assignments.FirstOrDefault(a => a.Id == assignmentId)
                         ?? throw new DomainException("That person is not assigned to this episode.");
        if (assignment.Responsibility == EpisodeResponsibility.Producer && assignment.UserId == ProducerId)
        {
            throw new DomainException("The episode's producer keeps the producer assignment. Choose a different producer first.");
        }
        _assignments.Remove(assignment);
        return assignment;
    }

    /// <summary>
    /// Deadlines the episode has not yet passed through, in order. The editing deadline is met once the
    /// episode reaches review, review once it awaits approval, approval once it is approved.
    /// </summary>
    public IEnumerable<(string Label, DateOnly Date)> PendingDeadlines()
    {
        if (IsCancelled)
        {
            yield break;
        }
        if (EditingDueDate is { } editing && Status < EpisodeStatus.InReview)
        {
            yield return ("Editing", editing);
        }
        if (ReviewDueDate is { } review && Status < EpisodeStatus.AwaitingApproval)
        {
            yield return ("Review", review);
        }
        if (ApprovalDueDate is { } approval && Status < EpisodeStatus.Approved)
        {
            yield return ("Approval", approval);
        }
    }

    /// <summary>A pending deadline has passed, or a recording session ended without the episode being marked recorded.</summary>
    public bool IsOverdue(DateOnly today, DateTimeOffset now) =>
        PendingDeadlines().Any(d => d.Date < today)
        || (Status is EpisodeStatus.Planned or EpisodeStatus.Scheduled && RecordingEnd is { } end && end < now);

    public bool IsAssigned(Guid userId) => ProducerId == userId || _assignments.Any(a => a.UserId == userId);
}
