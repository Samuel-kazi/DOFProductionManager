using DOF.Application.Activity;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Shows;

/// <summary>Filters for episode lists. All optional.</summary>
public sealed record EpisodeQuery(
    Guid? ShowId = null,
    Guid? SeasonId = null,
    bool WithoutSeason = false,
    Guid? AssigneeId = null,
    IReadOnlyCollection<string>? Statuses = null,
    bool? Open = null,
    string? Search = null);

/// <summary>Episodes: production details, schedule, crew, lifecycle and history.</summary>
public sealed class EpisodeService(
    IApplicationDbContext db,
    ShowAccess access,
    ShowReaders readers,
    ShowService shows,
    WorkReaders work,
    Releases.ReleaseReaders releaseReaders,
    ActivityRecorder activity,
    ActivityQueries history,
    ILogger<EpisodeService> logger)
{
    public async Task<PagedResponse<EpisodeSummaryResponse>> ListAsync(EpisodeQuery filter, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = Apply(db.Episodes, filter);
        var total = await query.CountAsync(ct);
        var ids = await query
            .OrderBy(e => e.Show.Name).ThenBy(e => e.ShowId)
            .ThenBy(e => e.Season == null).ThenBy(e => e.Season!.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber == null).ThenBy(e => e.EpisodeNumber)
            .ThenBy(e => e.RecordingStart).ThenBy(e => e.Id)
            .Skip((p - 1) * size).Take(size).Select(e => e.Id).ToListAsync(ct);
        var items = await readers.EpisodesAsync(db.Episodes.Where(e => ids.Contains(e.Id)), ct);
        return new PagedResponse<EpisodeSummaryResponse>(items, p, size, total);
    }

    /// <summary>Open episodes the user produces or is assigned to, soonest recording first.</summary>
    public async Task<IReadOnlyList<EpisodeSummaryResponse>> MineAsync(Guid userId, int limit, CancellationToken ct)
    {
        var items = await readers.EpisodesAsync(Apply(db.Episodes, new EpisodeQuery(AssigneeId: userId, Open: true)), ct);
        return [.. items.OrderBy(e => e.RecordingStart ?? DateTimeOffset.MaxValue).ThenBy(e => e.NextDeadline ?? DateOnly.MaxValue)
            .Take(limit)];
    }

    public async Task<IReadOnlyList<EpisodeSummaryResponse>> ForShowAsync(Guid showId, CancellationToken ct)
    {
        if (!await db.Shows.AnyAsync(x => x.Id == showId, ct))
        {
            throw new NotFoundException("Show", showId);
        }
        return await readers.EpisodesAsync(db.Episodes.Where(e => e.ShowId == showId), ct);
    }

    public async Task<IReadOnlyList<EpisodeSummaryResponse>> ForSeasonAsync(Guid seasonId, CancellationToken ct)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == seasonId, ct))
        {
            throw new NotFoundException("Season", seasonId);
        }
        return await readers.EpisodesAsync(db.Episodes.Where(e => e.SeasonId == seasonId), ct);
    }

    public async Task<EpisodeResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Episodes.AsNoTracking()
                    .Include(x => x.Show).Include(x => x.Season)
                    .Include(x => x.Assignments).ThenInclude(a => a.User)
                    .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Episode", id);
        var names = await work.NamesAsync([e.ProducerId, e.CreatedBy], ct);
        var tasks = await work.TasksAsync(WorkReaders.Ordered(db.Tasks.Where(t => t.EpisodeId == id)), ct);
        var releases = await releaseReaders.ReleasesAsync(Releases.ReleaseReaders.Ordered(db.Releases.Where(r => r.EpisodeId == id)), ct);
        var assigneeIds = e.Assignments.Select(a => a.UserId).ToList();

        return new EpisodeResponse(e.Id, new ShowRefResponse(e.ShowId, e.Show.Name), ShowReaders.Ref(e.Season), e.EpisodeNumber,
            ShowReaders.Code(e), e.Title, e.Description, WorkReaders.Summary(e.ProducerId, names),
            e.RecordingStart, e.RecordingEnd, e.RecordingLocation, e.RecordedAt,
            e.EditingDueDate, e.ReviewDueDate, e.ApprovalDueDate, e.Status.ToString(), e.ArchivedAt,
            e.IsOverdue(work.Today, work.Now),
            [.. e.Assignments
                .OrderBy(a => a.Responsibility).ThenBy(a => a.User.FullName)
                .Select(a => new EpisodeAssignmentResponse(a.Id, a.UserId, a.User.FullName, a.Responsibility.ToString(), a.AssignedAt))],
            ShowReaders.Timeline(e, work.OrganisationZone), tasks, releases,
            WorkReaders.Summary(e.CreatedBy, names), e.CreatedAt, e.UpdatedAt, e.Version,
            access.CanEditEpisode(e.Show.ProducerId, e.ProducerId),
            access.CanChangeEpisodeStatus(e.Show.ProducerId, e.ProducerId, assigneeIds));
    }

    public async Task<IReadOnlyList<ActivityEntryResponse>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Episodes.AnyAsync(e => e.Id == id, ct))
        {
            throw new NotFoundException("Episode", id);
        }
        return await history.ForEntityAsync(ActivityEntities.Episode, id, ct);
    }

    /// <summary>Creates an episode in a season (numbered next when no number is given).</summary>
    public async Task<EpisodeResponse> CreateInSeasonAsync(Guid seasonId, CreateEpisodeRequest request, CancellationToken ct)
    {
        var season = await db.Seasons.Include(s => s.Show).SingleOrDefaultAsync(s => s.Id == seasonId, ct)
                     ?? throw new NotFoundException("Season", seasonId);
        if (season.Status == SeasonStatus.Cancelled)
        {
            throw new DomainException("This season is cancelled. Restore it before adding episodes.");
        }
        var number = request.EpisodeNumber
                     ?? (await db.Episodes.Where(e => e.SeasonId == seasonId).MaxAsync(e => e.EpisodeNumber, ct) ?? 0) + 1;
        return await CreateAsync(season.Show, season, number, request, ct);
    }

    /// <summary>Creates an episode directly under a show, for one-off productions without seasons.</summary>
    public async Task<EpisodeResponse> CreateInShowAsync(Guid showId, CreateEpisodeRequest request, CancellationToken ct)
    {
        var show = await db.Shows.SingleOrDefaultAsync(x => x.Id == showId, ct) ?? throw new NotFoundException("Show", showId);
        return await CreateAsync(show, null, request.EpisodeNumber, request, ct);
    }

    private async Task<EpisodeResponse> CreateAsync(Show show, Season? season, int? number, CreateEpisodeRequest request, CancellationToken ct)
    {
        if (!access.CanEditShow(show.ProducerId))
        {
            throw new ForbiddenException();
        }
        await shows.EnsureActiveUsersAsync([request.ProducerId], ct);
        await EnsureNumberFreeAsync(show.Id, season?.Id, number, null, ct);

        var episode = Episode.Create(show, season, number, request.Title, request.Description, request.ProducerId ?? show.ProducerId);
        episode.ScheduleRecording(request.RecordingStart, request.RecordingEnd, request.RecordingLocation, show.DefaultDurationMinutes);
        episode.SetDeadlines(request.EditingDueDate, request.ReviewDueDate, request.ApprovalDueDate);
        if (episode.ProducerId is { } producer)
        {
            episode.Assign(producer, EpisodeResponsibility.Producer, work.Now);
        }

        db.Episodes.Add(episode);
        activity.Record(ActivityEntities.Episode, episode.Id, ActivityActions.Created, null,
            new { episode.Title, Show = show.Name, Season = season?.DisplayName, episode.EpisodeNumber, episode.RecordingStart });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Episode {EpisodeId} created in show {ShowId} by {UserId}", episode.Id, show.Id, access.Me);
        return await GetAsync(episode.Id, ct);
    }

    public async Task<EpisodeResponse> UpdateAsync(Guid id, UpdateEpisodeRequest request, CancellationToken ct)
    {
        var episode = await LoadAsync(id, request.Version, ct);
        EnsureCanEdit(episode);
        episode.Show.EnsureNotArchived();
        await shows.EnsureActiveUsersAsync([request.ProducerId], ct);

        Season? season = null;
        if (request.SeasonId is { } seasonId)
        {
            season = await db.Seasons.SingleOrDefaultAsync(s => s.Id == seasonId, ct)
                     ?? throw new DomainException("The selected season does not exist.");
        }
        if (request.SeasonId != episode.SeasonId || request.EpisodeNumber != episode.EpisodeNumber)
        {
            await EnsureNumberFreeAsync(episode.ShowId, request.SeasonId, request.EpisodeNumber, id, ct);
        }

        var before = Snapshot(episode);
        var previousRecording = (episode.RecordingStart, episode.RecordingEnd);
        var previousDeadlines = Deadlines(episode);

        episode.Place(episode.Show, season, request.EpisodeNumber);
        episode.Describe(request.Title, request.Description, request.ProducerId);
        episode.ScheduleRecording(request.RecordingStart, request.RecordingEnd, request.RecordingLocation, episode.Show.DefaultDurationMinutes);
        episode.SetDeadlines(request.EditingDueDate, request.ReviewDueDate, request.ApprovalDueDate);
        if (episode.ProducerId is { } producer)
        {
            episode.Assign(producer, EpisodeResponsibility.Producer, work.Now);
        }

        if (previousRecording != (episode.RecordingStart, episode.RecordingEnd))
        {
            activity.Record(ActivityEntities.Episode, id, ActivityActions.Rescheduled,
                new { RecordingStart = previousRecording.RecordingStart, RecordingEnd = previousRecording.RecordingEnd },
                new { episode.RecordingStart, episode.RecordingEnd });
        }
        var deadlines = Deadlines(episode);
        foreach (var key in deadlines.Keys.Where(k => !Equals(previousDeadlines[k], deadlines[k])))
        {
            activity.Record(ActivityEntities.Episode, id, ActivityActions.Rescheduled,
                new Dictionary<string, object?> { [key] = previousDeadlines[key] },
                new Dictionary<string, object?> { [key] = deadlines[key] });
        }
        activity.RecordChanges(ActivityEntities.Episode, id, ActivityActions.Updated, before, Snapshot(episode));

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Sets a production status (corrections included; history keeps every change). Publication statuses
    /// come from release confirmations and cannot be set here.
    /// </summary>
    public async Task<EpisodeResponse> ChangeStatusAsync(Guid id, ChangeEpisodeStatusRequest request, CancellationToken ct)
    {
        var episode = await LoadAsync(id, request.Version, ct);
        if (!access.CanChangeEpisodeStatus(episode.Show.ProducerId, episode.ProducerId, episode.Assignments.Select(a => a.UserId)))
        {
            throw new ForbiddenException();
        }
        var status = WorkParsing.Parse<EpisodeStatus>(request.Status, "episode status");
        if (Episode.IsPublicationStatus(status) || Episode.IsPublicationStatus(episode.Status))
        {
            throw new DomainException("Publication status follows release confirmations and cannot be set by hand.");
        }
        if (status != EpisodeStatus.Cancelled)
        {
            episode.Show.EnsureNotArchived();
        }
        if (status == episode.Status && request.RecordedAt is null)
        {
            return await GetAsync(id, ct);
        }

        var previous = episode.Status;
        var previousRecordedAt = episode.RecordedAt;
        episode.ChangeStatus(status, work.Now, request.RecordedAt);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (previous != status)
        {
            activity.Record(ActivityEntities.Episode, id, ActivityActions.StatusChanged,
                new { Status = previous.ToString() }, new { Status = status.ToString(), Note = note });
        }
        if (previousRecordedAt != episode.RecordedAt)
        {
            activity.Record(ActivityEntities.Episode, id, ActivityActions.Updated,
                new { RecordedAt = previousRecordedAt }, new { episode.RecordedAt });
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Episode {EpisodeId} status {From} -> {To} by {UserId}", id, previous, status, access.Me);
        return await GetAsync(id, ct);
    }

    public async Task<EpisodeResponse> AssignAsync(Guid id, AssignEpisodeRequest request, CancellationToken ct)
    {
        var episode = await LoadAsync(id, null, ct);
        EnsureCanEdit(episode);
        episode.Show.EnsureNotArchived();
        if (episode.IsCancelled)
        {
            throw new DomainException("This episode is cancelled. Restore it before changing its crew.");
        }
        await shows.EnsureActiveUsersAsync([request.UserId], ct);
        var responsibility = WorkParsing.Parse<EpisodeResponsibility>(request.Responsibility, "responsibility");

        if (episode.Assign(request.UserId, responsibility, work.Now))
        {
            var name = await db.Users.Where(u => u.Id == request.UserId).Select(u => u.FullName).SingleAsync(ct);
            activity.Record(ActivityEntities.Episode, id, ActivityActions.MemberAdded, null,
                new { Member = name, Role = responsibility.ToString() });
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<EpisodeResponse> UnassignAsync(Guid id, Guid assignmentId, CancellationToken ct)
    {
        var episode = await LoadAsync(id, null, ct);
        EnsureCanEdit(episode);
        var removed = episode.Unassign(assignmentId);
        var name = await db.Users.Where(u => u.Id == removed.UserId).Select(u => u.FullName).SingleAsync(ct);
        activity.Record(ActivityEntities.Episode, id, ActivityActions.MemberRemoved,
            new { Member = name, Role = removed.Responsibility.ToString() }, null);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- helpers ----------

    internal IQueryable<Episode> Apply(IQueryable<Episode> query, EpisodeQuery filter)
    {
        query = query.AsNoTracking();
        if (filter.ShowId is { } show)
        {
            query = query.Where(e => e.ShowId == show);
        }
        if (filter.SeasonId is { } season)
        {
            query = query.Where(e => e.SeasonId == season);
        }
        if (filter.WithoutSeason)
        {
            query = query.Where(e => e.SeasonId == null);
        }
        if (filter.AssigneeId is { } user)
        {
            query = query.Where(e => e.ProducerId == user || e.Assignments.Any(a => a.UserId == user));
        }
        if (WorkParsing.ParseAll<EpisodeStatus>(filter.Statuses) is { } statuses)
        {
            query = query.Where(e => statuses.Contains(e.Status));
        }
        if (filter.Open is { } open)
        {
            query = open
                ? query.Where(e => e.Status != EpisodeStatus.Published && e.Status != EpisodeStatus.Cancelled
                                   && e.Show.Status != ShowStatus.Archived)
                : query.Where(e => e.Status == EpisodeStatus.Published || e.Status == EpisodeStatus.Cancelled);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(e => e.Title.ToLower().Contains(term) || e.Show.Name.ToLower().Contains(term));
        }
        return query;
    }

    private async Task<Episode> LoadAsync(Guid id, int? version, CancellationToken ct)
    {
        var episode = await db.Episodes.Include(e => e.Show).Include(e => e.Season).Include(e => e.Assignments)
                          .SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Episode", id);
        if (version is { } expected)
        {
            if (episode.Version != expected)
            {
                throw new ConflictException("This episode was modified by someone else. Reload the latest version and try again.");
            }
            db.Entry(episode).Property(e => e.Version).OriginalValue = expected;
        }
        return episode;
    }

    private void EnsureCanEdit(Episode episode)
    {
        if (!access.CanEditEpisode(episode.Show.ProducerId, episode.ProducerId))
        {
            throw new ForbiddenException();
        }
    }

    private async Task EnsureNumberFreeAsync(Guid showId, Guid? seasonId, int? number, Guid? except, CancellationToken ct)
    {
        if (number is null)
        {
            return;
        }
        var taken = seasonId is null
            ? await db.Episodes.AnyAsync(e => e.ShowId == showId && e.SeasonId == null && e.EpisodeNumber == number && e.Id != except, ct)
            : await db.Episodes.AnyAsync(e => e.SeasonId == seasonId && e.EpisodeNumber == number && e.Id != except, ct);
        if (taken)
        {
            throw new ConflictException(seasonId is null
                ? $"This show already has an episode {number} outside its seasons."
                : $"This season already has an episode {number}.");
        }
    }

    private static Dictionary<string, object?> Deadlines(Episode e) => new()
    {
        ["EditingDueDate"] = e.EditingDueDate, ["ReviewDueDate"] = e.ReviewDueDate, ["ApprovalDueDate"] = e.ApprovalDueDate,
    };

    private static Dictionary<string, object?> Snapshot(Episode e) => new()
    {
        ["Title"] = e.Title, ["Description"] = e.Description, ["Season"] = e.Season?.DisplayName,
        ["EpisodeNumber"] = e.EpisodeNumber, ["Producer"] = e.ProducerId, ["RecordingLocation"] = e.RecordingLocation,
    };
}
