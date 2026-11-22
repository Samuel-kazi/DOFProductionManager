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

/// <summary>Shows and their seasons.</summary>
public sealed class ShowService(
    IApplicationDbContext db,
    ShowAccess access,
    ShowReaders readers,
    WorkReaders work,
    ActivityRecorder activity,
    ActivityQueries history,
    ILogger<ShowService> logger)
{
    // ---------- shows ----------

    public async Task<PagedResponse<ShowSummaryResponse>> ListAsync(int? page, int? pageSize, string? search,
        IReadOnlyCollection<string>? statuses, string? showType, Guid? producerId, bool includeArchived, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.Shows.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Name.ToLower().Contains(term));
        }
        if (WorkParsing.ParseAll<ShowStatus>(statuses) is { } wanted)
        {
            query = query.Where(x => wanted.Contains(x.Status));
        }
        else if (!includeArchived)
        {
            query = query.Where(x => x.Status != ShowStatus.Archived);
        }
        if (showType is not null)
        {
            var type = WorkParsing.Parse<ShowType>(showType, "show type");
            query = query.Where(x => x.ShowType == type);
        }
        if (producerId is not null)
        {
            query = query.Where(x => x.ProducerId == producerId);
        }

        var total = await query.CountAsync(ct);
        var now = work.Now;
        var items = await query
            .OrderBy(x => x.Status == ShowStatus.Archived).ThenBy(x => x.Status == ShowStatus.Ended).ThenBy(x => x.Name)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new ShowSummaryResponse(
                x.Id, x.Name, x.ShowType.ToString(), x.Status.ToString(),
                db.Users.Where(u => u.Id == x.ProducerId).Select(u => new UserSummaryResponse(u.Id, u.FullName)).FirstOrDefault(),
                db.Seasons.Count(s => s.ShowId == x.Id),
                db.Episodes.Count(e => e.ShowId == x.Id && e.Status != EpisodeStatus.Cancelled),
                db.Episodes.Count(e => e.ShowId == x.Id && e.Status != EpisodeStatus.Cancelled
                                       && e.Status != EpisodeStatus.Published && e.Status != EpisodeStatus.Planned),
                db.Episodes.Where(e => e.ShowId == x.Id && e.Status != EpisodeStatus.Cancelled && e.RecordingStart >= now)
                    .OrderBy(e => e.RecordingStart).Select(e => e.RecordingStart).FirstOrDefault(),
                db.Episodes.Where(e => e.ShowId == x.Id && e.Status != EpisodeStatus.Cancelled && e.RecordingStart >= now)
                    .OrderBy(e => e.RecordingStart).Select(e => e.Title).FirstOrDefault()))
            .ToListAsync(ct);
        return new PagedResponse<ShowSummaryResponse>(items, p, size, total);
    }

    public async Task<ShowResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var show = await db.Shows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw new NotFoundException("Show", id);
        var names = await work.NamesAsync([show.ProducerId, show.CreatedBy], ct);
        return new ShowResponse(show.Id, show.Name, show.Description, show.ShowType.ToString(), show.Status.ToString(),
            WorkReaders.Summary(show.ProducerId, names), show.DefaultDurationMinutes, show.ArchivedAt,
            await readers.SeasonsAsync(id, show.ProducerId, ct), WorkReaders.Summary(show.CreatedBy, names),
            show.CreatedAt, show.UpdatedAt, show.Version, access.CanEditShow(show.ProducerId), access.CanManage);
    }

    public async Task<ShowResponse> CreateAsync(CreateShowRequest request, CancellationToken ct)
    {
        if (!access.CanManage)
        {
            throw new ForbiddenException("Creating shows needs the episodes.manage permission.");
        }
        await EnsureActiveUsersAsync([request.ProducerId], ct);

        var show = Show.Create(request.Name, request.Description, WorkParsing.Parse<ShowType>(request.ShowType, "show type"),
            request.ProducerId, request.DefaultDurationMinutes);
        db.Shows.Add(show);
        activity.Record(ActivityEntities.Show, show.Id, ActivityActions.Created, null,
            new { show.Name, Type = show.ShowType.ToString() });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Show {ShowId} created by {UserId}", show.Id, access.Me);
        return await GetAsync(show.Id, ct);
    }

    public async Task<ShowResponse> UpdateAsync(Guid id, UpdateShowRequest request, CancellationToken ct)
    {
        var show = await LoadShowAsync(id, request.Version, ct);
        EnsureCanEdit(show);
        await EnsureActiveUsersAsync([request.ProducerId], ct);

        var before = Snapshot(show);
        show.Update(request.Name, request.Description, WorkParsing.Parse<ShowType>(request.ShowType, "show type"),
            request.ProducerId, request.DefaultDurationMinutes);
        activity.RecordChanges(ActivityEntities.Show, id, ActivityActions.Updated, before, Snapshot(show));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Archiving and restoring need episodes.manage; other status changes need edit rights.</summary>
    public async Task<ShowResponse> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct)
    {
        var show = await LoadShowAsync(id, request.Version, ct);
        var status = WorkParsing.Parse<ShowStatus>(request.Status, "show status");
        if (status == show.Status)
        {
            return await GetAsync(id, ct);
        }

        var archiving = status == ShowStatus.Archived || show.Status == ShowStatus.Archived;
        if (archiving ? !access.CanManage : !access.CanEditShow(show.ProducerId))
        {
            throw new ForbiddenException(archiving
                ? "Archiving or restoring a show needs the episodes.manage permission."
                : "You don't have permission to do that.");
        }

        var previous = show.Status;
        show.ChangeStatus(status, work.Now);
        activity.Record(ActivityEntities.Show, id, ActivityActions.StatusChanged,
            new { Status = previous.ToString() }, new { Status = status.ToString() });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Show {ShowId} status {From} -> {To} by {UserId}", id, previous, status, access.Me);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<ActivityEntryResponse>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Shows.AnyAsync(x => x.Id == id, ct))
        {
            throw new NotFoundException("Show", id);
        }
        var seasonIds = await db.Seasons.Where(s => s.ShowId == id).Select(s => s.Id).ToListAsync(ct);
        return await history.GetAsync(a => (a.EntityType == ActivityEntities.Show && a.EntityId == id)
                                           || (a.EntityType == ActivityEntities.Season && seasonIds.Contains(a.EntityId)), ct);
    }

    // ---------- seasons ----------

    public async Task<IReadOnlyList<SeasonResponse>> ListSeasonsAsync(Guid showId, CancellationToken ct)
    {
        var producer = await db.Shows.Where(x => x.Id == showId).Select(x => new { x.ProducerId }).SingleOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Show", showId);
        return await readers.SeasonsAsync(showId, producer.ProducerId, ct);
    }

    public async Task<SeasonResponse> GetSeasonAsync(Guid id, CancellationToken ct)
    {
        var season = await db.Seasons.AsNoTracking().Include(s => s.Show).SingleOrDefaultAsync(s => s.Id == id, ct)
                     ?? throw new NotFoundException("Season", id);
        return (await readers.SeasonsAsync(season.ShowId, season.Show.ProducerId, ct)).Single(s => s.Id == id);
    }

    public async Task<SeasonResponse> CreateSeasonAsync(Guid showId, CreateSeasonRequest request, CancellationToken ct)
    {
        var show = await db.Shows.SingleOrDefaultAsync(x => x.Id == showId, ct) ?? throw new NotFoundException("Show", showId);
        EnsureCanEdit(show);
        show.EnsureNotArchived();

        var number = request.SeasonNumber
                     ?? (await db.Seasons.Where(s => s.ShowId == showId).MaxAsync(s => (int?)s.SeasonNumber, ct) ?? 0) + 1;
        await EnsureSeasonNumberFreeAsync(showId, number, null, ct);

        var season = Season.Create(showId, number, request.Name, request.StartDate, request.EndDate);
        db.Seasons.Add(season);
        activity.Record(ActivityEntities.Season, season.Id, ActivityActions.Created, null,
            new { Season = season.DisplayName, Show = show.Name });
        await db.SaveChangesAsync(ct);
        return await GetSeasonAsync(season.Id, ct);
    }

    public async Task<SeasonResponse> UpdateSeasonAsync(Guid id, UpdateSeasonRequest request, CancellationToken ct)
    {
        var season = await db.Seasons.Include(s => s.Show).SingleOrDefaultAsync(s => s.Id == id, ct)
                     ?? throw new NotFoundException("Season", id);
        if (season.Version != request.Version)
        {
            throw new ConflictException("This season was modified by someone else. Reload the latest version and try again.");
        }
        db.Entry(season).Property(s => s.Version).OriginalValue = request.Version;
        EnsureCanEdit(season.Show);
        season.Show.EnsureNotArchived();
        if (request.SeasonNumber != season.SeasonNumber)
        {
            await EnsureSeasonNumberFreeAsync(season.ShowId, request.SeasonNumber, id, ct);
        }

        var before = SeasonSnapshot(season);
        season.Update(request.SeasonNumber, request.Name, request.StartDate, request.EndDate,
            WorkParsing.Parse<SeasonStatus>(request.Status, "season status"));
        var after = SeasonSnapshot(season);
        if (!Equals(before["Status"], after["Status"]))
        {
            activity.Record(ActivityEntities.Season, id, ActivityActions.StatusChanged,
                new { Status = before["Status"] }, new { Status = after["Status"] });
            before.Remove("Status");
            after.Remove("Status");
        }
        activity.RecordChanges(ActivityEntities.Season, id, ActivityActions.Updated, before, after);
        await db.SaveChangesAsync(ct);
        return await GetSeasonAsync(id, ct);
    }

    // ---------- helpers ----------

    private async Task<Show> LoadShowAsync(Guid id, int? version, CancellationToken ct)
    {
        var show = await db.Shows.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Show", id);
        if (version is { } expected)
        {
            if (show.Version != expected)
            {
                throw new ConflictException("This show was modified by someone else. Reload the latest version and try again.");
            }
            db.Entry(show).Property(x => x.Version).OriginalValue = expected;
        }
        return show;
    }

    private void EnsureCanEdit(Show show)
    {
        if (!access.CanEditShow(show.ProducerId))
        {
            throw new ForbiddenException();
        }
    }

    private async Task EnsureSeasonNumberFreeAsync(Guid showId, int number, Guid? except, CancellationToken ct)
    {
        if (await db.Seasons.AnyAsync(s => s.ShowId == showId && s.SeasonNumber == number && s.Id != except, ct))
        {
            throw new ConflictException($"This show already has a season {number}.");
        }
    }

    internal async Task EnsureActiveUsersAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count > 0 && await db.Users.CountAsync(u => wanted.Contains(u.Id) && u.IsActive, ct) != wanted.Count)
        {
            throw new DomainException("Shows and episodes can only be given to existing, active users.");
        }
    }

    private static Dictionary<string, object?> Snapshot(Show s) => new()
    {
        ["Name"] = s.Name, ["Description"] = s.Description, ["ShowType"] = s.ShowType.ToString(),
        ["Producer"] = s.ProducerId, ["DefaultDurationMinutes"] = s.DefaultDurationMinutes,
    };

    private static Dictionary<string, object?> SeasonSnapshot(Season s) => new()
    {
        ["SeasonNumber"] = s.SeasonNumber, ["Name"] = s.Name, ["StartDate"] = s.StartDate, ["EndDate"] = s.EndDate,
        ["Status"] = s.Status.ToString(),
    };
}
