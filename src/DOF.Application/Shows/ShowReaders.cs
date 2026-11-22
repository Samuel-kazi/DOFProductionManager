using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Shows;

/// <summary>Loads seasons and episodes as responses, resolving names and permissions in batches.</summary>
public sealed class ShowReaders(IApplicationDbContext db, ShowAccess access, WorkReaders work)
{
    public static SeasonRefResponse? Ref(Season? season) =>
        season is null ? null : new SeasonRefResponse(season.Id, season.SeasonNumber, season.DisplayName);

    public static string Code(Episode episode) => Episode.Code(episode.Season?.SeasonNumber, episode.EpisodeNumber);

    public async Task<List<SeasonResponse>> SeasonsAsync(Guid showId, Guid? showProducerId, CancellationToken ct)
    {
        var seasons = await db.Seasons.AsNoTracking().Where(s => s.ShowId == showId).OrderBy(s => s.SeasonNumber)
            .Select(s => new { Season = s, Episodes = db.Episodes.Count(e => e.SeasonId == s.Id) })
            .ToListAsync(ct);
        var canEdit = access.CanEditShow(showProducerId);
        return seasons.Select(x => new SeasonResponse(x.Season.Id, x.Season.ShowId, x.Season.SeasonNumber, x.Season.Name,
            x.Season.DisplayName, x.Season.StartDate, x.Season.EndDate, x.Season.Status.ToString(), x.Episodes,
            x.Season.Version, canEdit)).ToList();
    }

    /// <summary>Episodes in production order: by season, then number, then recording time, then title.</summary>
    public async Task<List<EpisodeSummaryResponse>> EpisodesAsync(IQueryable<Episode> query, CancellationToken ct)
    {
        var episodes = await query.AsNoTracking().Include(e => e.Show).Include(e => e.Season).ToListAsync(ct);
        var names = await work.NamesAsync(episodes.Select(e => e.ProducerId), ct);
        var today = work.Today;
        var now = work.Now;
        return episodes
            .OrderBy(e => e.Show.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.ShowId)
            .ThenBy(e => e.Season is null).ThenBy(e => e.Season?.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber is null).ThenBy(e => e.EpisodeNumber)
            .ThenBy(e => e.RecordingStart ?? DateTimeOffset.MaxValue).ThenBy(e => e.Title)
            .Select(e => Summary(e, names, today, now)).ToList();
    }

    public static EpisodeSummaryResponse Summary(Episode e, IReadOnlyDictionary<Guid, string> names, DateOnly today, DateTimeOffset now)
    {
        var next = e.PendingDeadlines().Select(d => ((string Label, DateOnly Date)?)d).FirstOrDefault();
        return new EpisodeSummaryResponse(e.Id, e.ShowId, e.Show.Name, Ref(e.Season), e.EpisodeNumber, Code(e), e.Title,
            e.Status.ToString(), WorkReaders.Summary(e.ProducerId, names), e.RecordingStart, e.RecordingEnd,
            next?.Label, next?.Date, e.IsOverdue(today, now));
    }

    /// <summary>
    /// The production timeline: Planned, Recording, Editing, Review, Approval, Release preparation, Published.
    /// Stages before the current one are done; a cancelled episode's unfinished stages are cancelled.
    /// </summary>
    public static IReadOnlyList<EpisodeStageResponse> Timeline(Episode e, TimeZoneInfo zone)
    {
        // Index of the stage the episode is in, and whether that stage itself is finished.
        var (current, finished) = e.Status switch
        {
            EpisodeStatus.Planned => (0, false),
            EpisodeStatus.Scheduled => (1, false),
            EpisodeStatus.Recorded => (1, true),
            EpisodeStatus.Editing => (2, false),
            EpisodeStatus.InReview => (3, false),
            EpisodeStatus.AwaitingApproval => (4, false),
            EpisodeStatus.Approved => (4, true),
            EpisodeStatus.ReadyForRelease => (5, false),
            EpisodeStatus.PartiallyPublished => (6, false),
            EpisodeStatus.Published => (6, true),
            _ => (StageFromHistory(e), false),
        };

        (string Stage, DateOnly? Date)[] stages =
        [
            ("Planned", null),
            ("Recording", e.RecordedAt is { } r ? ZonedTime.LocalDate(r, zone)
                : e.RecordingStart is { } s ? ZonedTime.LocalDate(s, zone) : null),
            ("Editing", e.EditingDueDate),
            ("Review", e.ReviewDueDate),
            ("Approval", e.ApprovalDueDate),
            ("Release preparation", null),
            ("Published", null),
        ];

        return stages.Select((stage, i) => new EpisodeStageResponse(stage.Stage,
            i < current || (i == current && finished) ? ShowValues.StageDone
            : e.IsCancelled ? ShowValues.StageCancelled
            : i == current ? ShowValues.StageCurrent
            : ShowValues.StageUpcoming, stage.Date)).ToList();
    }

    // A cancelled episode keeps the recording stage as done if it had been recorded.
    private static int StageFromHistory(Episode e) => e.RecordedAt is null ? 1 : 2;
}
