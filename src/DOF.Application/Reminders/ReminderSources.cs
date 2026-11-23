using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Reminders;

/// <summary>
/// What a reminder points at, read live from the record: its title, its time (the "anchor" offsets count
/// back from), who it concerns, and whether it still needs reminders.
/// </summary>
public sealed record ReminderTarget(
    ReminderSourceType Type,
    Guid Id,
    string Title,
    DateTimeOffset? Anchor,
    IReadOnlyList<Guid> Recipients,
    bool IsActive,
    string Kind);

public sealed class ReminderSources(
    IApplicationDbContext db, IOptions<OrganisationOptions> organisation, IOptions<ReminderOptions> options)
{
    private TimeZoneInfo Zone => ZonedTime.Find(organisation.Value.DefaultTimeZone);

    /// <summary>Today in the organisation timezone (milestone due dates are calendar dates).</summary>
    public DateOnly Today(DateTimeOffset now) => ZonedTime.LocalDate(now, Zone);

    /// <summary>Date-only records are reminded about at a set local hour on their day.</summary>
    public DateTimeOffset DateAnchor(DateOnly date) =>
        ZonedTime.StartOfDay(date, Zone).AddHours(options.Value.DateOnlyReminderHour);

    public async Task<ReminderTarget?> FindAsync(ReminderSourceType type, Guid id, CancellationToken ct) => type switch
    {
        ReminderSourceType.Task => await db.Tasks.AsNoTracking().Where(t => t.Id == id)
            .Select(t => new ReminderTarget(type, t.Id, t.Title, t.DueAt, t.Assignments.Select(a => a.UserId).ToList(),
                t.DueAt != null && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled, "Task"))
            .SingleOrDefaultAsync(ct),
        ReminderSourceType.Milestone => await MilestoneAsync(id, ct),
        ReminderSourceType.CalendarEvent => await db.CalendarEvents.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new ReminderTarget(type, e.Id, e.Title, e.StartAt, e.Assignments.Select(a => a.UserId).ToList(),
                e.Status == CalendarEventStatus.Scheduled, "Event"))
            .SingleOrDefaultAsync(ct),
        ReminderSourceType.Episode => await db.Episodes.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id, e.Title, ShowName = e.Show.Name, e.RecordingStart, e.ProducerId, e.Status, ShowStatus = e.Show.Status,
                Crew = e.Assignments.Select(a => a.UserId).ToList(),
            })
            .SingleOrDefaultAsync(ct) is { } ep
            ? new ReminderTarget(type, ep.Id, $"{ep.ShowName}: {ep.Title}", ep.RecordingStart,
                [.. ep.Crew.Concat(ep.ProducerId is { } p ? [p] : []).Distinct()],
                ep.RecordingStart != null && ep.ShowStatus != ShowStatus.Archived
                && ep.Status is EpisodeStatus.Planned or EpisodeStatus.Scheduled, "Recording")
            : null,
        ReminderSourceType.Release => await db.Releases.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, ShowName = r.Episode != null ? r.Episode.Show.Name : null,
                r.ScheduledAt, r.Status, r.AssignedTo, ProducerId = r.Episode != null ? r.Episode.ProducerId : r.CreatedBy,
            })
            .SingleOrDefaultAsync(ct) is { } rel
            ? new ReminderTarget(type, rel.Id, ReleaseTitle(rel.Channel, rel.ShowName, rel.Title), rel.ScheduledAt,
                rel.AssignedTo is { } a ? [a] : rel.ProducerId is { } p ? [p] : [],
                rel.ScheduledAt != null && Release.IsOpenStatus(rel.Status), "Release")
            : null,
        _ => null,
    };

    /// <summary>Titles and times for many records at once: one query per record type (used by reminder lists).</summary>
    public async Task<Dictionary<(ReminderSourceType, Guid), (string Title, DateTimeOffset? At)>> DescribeAsync(
        IEnumerable<(ReminderSourceType Type, Guid Id)> keys, CancellationToken ct)
    {
        var result = new Dictionary<(ReminderSourceType, Guid), (string, DateTimeOffset?)>();
        foreach (var group in keys.Distinct().GroupBy(k => k.Type))
        {
            var ids = group.Select(k => k.Id).ToList();
            var rows = group.Key switch
            {
                ReminderSourceType.Task => (await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id))
                    .Select(t => new { t.Id, t.Title, t.DueAt }).ToListAsync(ct)).Select(t => (t.Id, t.Title, t.DueAt)).ToList(),
                ReminderSourceType.Milestone => (await db.ProjectMilestones.AsNoTracking().Where(m => ids.Contains(m.Id))
                    .Select(m => new { m.Id, m.Title, Project = m.Project.Name, m.DueDate }).ToListAsync(ct))
                    .Select(m => (m.Id, $"{m.Title} · {m.Project}", (DateTimeOffset?)DateAnchor(m.DueDate))).ToList(),
                ReminderSourceType.CalendarEvent => (await db.CalendarEvents.AsNoTracking().Where(e => ids.Contains(e.Id))
                    .Select(e => new { e.Id, e.Title, e.StartAt }).ToListAsync(ct)).Select(e => (e.Id, e.Title, (DateTimeOffset?)e.StartAt)).ToList(),
                ReminderSourceType.Episode => (await db.Episodes.AsNoTracking().Where(e => ids.Contains(e.Id))
                    .Select(e => new { e.Id, e.Title, Show = e.Show.Name, e.RecordingStart }).ToListAsync(ct))
                    .Select(e => (e.Id, $"{e.Show}: {e.Title}", e.RecordingStart)).ToList(),
                ReminderSourceType.Release => (await db.Releases.AsNoTracking().Where(r => ids.Contains(r.Id))
                    .Select(r => new
                    {
                        r.Id, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title,
                        Show = r.Episode != null ? r.Episode.Show.Name : null, r.ScheduledAt,
                    }).ToListAsync(ct))
                    .Select(r => (r.Id, ReleaseTitle(r.Channel, r.Show, r.Title), r.ScheduledAt)).ToList(),
                _ => [],
            };
            foreach (var (id, title, at) in rows)
            {
                result[(group.Key, id)] = (title, at);
            }
        }
        return result;
    }

    private async Task<ReminderTarget?> MilestoneAsync(Guid id, CancellationToken ct)
    {
        var m = await db.ProjectMilestones.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Title, x.DueDate, x.AssignedTo, x.Status, ProjectStatus = x.Project.Status, ProjectName = x.Project.Name })
            .SingleOrDefaultAsync(ct);
        return m is null
            ? null
            : new ReminderTarget(ReminderSourceType.Milestone, m.Id, $"{m.Title} · {m.ProjectName}", DateAnchor(m.DueDate),
                m.AssignedTo is { } a ? [a] : [],
                m.Status is not (WorkStatus.Completed or WorkStatus.Cancelled) && m.ProjectStatus != ProjectStatus.Archived, "Milestone");
    }

    /// <summary>Every record with a time between now and the horizon, optionally only those concerning one person.</summary>
    public async Task<List<(ReminderSourceType Type, Guid Id)>> UpcomingAsync(DateTimeOffset now, DateTimeOffset until, Guid? userId,
        CancellationToken ct)
    {
        var today = ZonedTime.LocalDate(now, Zone);
        var lastDay = ZonedTime.LocalDate(until, Zone);
        var tasks = await db.Tasks.Where(t => t.DueAt > now && t.DueAt <= until
                                              && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                              && (userId == null || t.Assignments.Any(a => a.UserId == userId)))
            .Select(t => t.Id).ToListAsync(ct);
        var milestones = await db.ProjectMilestones.Where(m => m.DueDate >= today && m.DueDate <= lastDay
                                                               && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled
                                                               && (userId == null || m.AssignedTo == userId))
            .Select(m => m.Id).ToListAsync(ct);
        var events = await db.CalendarEvents.Where(e => e.StartAt > now && e.StartAt <= until && e.Status == CalendarEventStatus.Scheduled
                                                        && (userId == null || e.Assignments.Any(a => a.UserId == userId)))
            .Select(e => e.Id).ToListAsync(ct);
        var episodes = await db.Episodes.Where(e => e.RecordingStart > now && e.RecordingStart <= until
                                                    && (e.Status == EpisodeStatus.Planned || e.Status == EpisodeStatus.Scheduled)
                                                    && (userId == null || e.ProducerId == userId || e.Assignments.Any(a => a.UserId == userId)))
            .Select(e => e.Id).ToListAsync(ct);
        var releases = await db.Releases.Where(r => r.ScheduledAt > now && r.ScheduledAt <= until
                                                    && r.Status != ReleaseStatus.Published && r.Status != ReleaseStatus.Cancelled
                                                    && (userId == null || r.AssignedTo == userId
                                                        || (r.AssignedTo == null && (r.Episode != null ? r.Episode.ProducerId : r.CreatedBy) == userId)))
            .Select(r => r.Id).ToListAsync(ct);

        return
        [
            .. tasks.Select(id => (ReminderSourceType.Task, id)),
            .. milestones.Select(id => (ReminderSourceType.Milestone, id)),
            .. events.Select(id => (ReminderSourceType.CalendarEvent, id)),
            .. episodes.Select(id => (ReminderSourceType.Episode, id)),
            .. releases.Select(id => (ReminderSourceType.Release, id)),
        ];
    }

    /// <summary>"YouTube release · Show: Title", or "YouTube release · Title" for content without an episode.</summary>
    private static string ReleaseTitle(string channel, string? show, string title) =>
        show is null ? $"{channel} release · {title}" : $"{channel} release · {show}: {title}";
}
