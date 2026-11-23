using DOF.Application.Calendar;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Reports;

/// <summary>
/// Management, workload and publication reports (spec 5.10). Read-only, server-side queries over the live
/// records, formatted in the requesting person's timezone (or an explicit one).
/// </summary>
public sealed class ReportService(
    IApplicationDbContext db,
    CalendarService calendar,
    WorkloadService workload,
    WorkReaders work,
    TimeProvider clock)
{
    public const int MaxRows = 2000;
    public const int MaxRangeDays = 400;

    private sealed record Context(TimeZoneInfo Zone, DateTimeOffset Now)
    {
        public string Time(DateTimeOffset? t) =>
            t is { } v ? TimeZoneInfo.ConvertTime(v, Zone).ToString(ReportValues.DateTimeFormat) : "";

        public static string Date(DateOnly? d) => d?.ToString(ReportValues.DateFormat) ?? "";
    }

    private async Task<Context> ContextAsync(string? timeZone, CancellationToken ct) =>
        new(await calendar.ResolveZoneAsync(timeZone, ct), clock.GetUtcNow());

    private static ReportResponse Build(string key, string description, string period, Context c, IReadOnlyList<ReportFigure> summary,
        IReadOnlyList<string> columns, IEnumerable<(IReadOnlyList<string> Row, ReportRowLink? Link)> rows)
    {
        var list = rows.Take(MaxRows).ToList();
        return new ReportResponse(key, ReportValues.Reports.Single(r => r.Key == key).Title, description, period, c.Zone.Id, c.Now,
            summary, columns, [.. list.Select(r => r.Row)], [.. list.Select(r => r.Link)]);
    }

    private static ReportRowLink Link(string type, Guid id) => new(type, id);

    // ---------- weekly / monthly ----------

    public async Task<ReportResponse> WeeklyAsync(DateOnly? date, string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var monday = ZonedTime.StartOfWeek(date ?? ZonedTime.LocalDate(c.Now, c.Zone));
        return await PeriodAsync("weekly", monday, monday.AddDays(7), $"Week of {monday:d MMM yyyy}", c, ct);
    }

    public async Task<ReportResponse> MonthlyAsync(int? year, int? month, string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var today = ZonedTime.LocalDate(c.Now, c.Zone);
        if (month is < 1 or > 12)
        {
            throw new DomainException("Month must be between 1 and 12.");
        }
        var first = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        return await PeriodAsync("monthly", first, first.AddMonths(1), first.ToString("MMMM yyyy"), c, ct);
    }

    /// <summary>What happened in [first, end): completed work, recordings, publications, misses, and what slipped.</summary>
    private async Task<ReportResponse> PeriodAsync(string key, DateOnly first, DateOnly end, string period, Context c, CancellationToken ct)
    {
        var from = ZonedTime.StartOfDay(first, c.Zone);
        var to = ZonedTime.StartOfDay(end, c.Zone);

        var tasksDone = await db.Tasks.AsNoTracking().Where(t => t.CompletedAt >= from && t.CompletedAt < to)
            .Select(t => new { t.Id, t.Title, t.CompletedAt, People = t.Assignments.Where(a => a.IsPrimary).Select(a => a.User.FullName).ToList() })
            .ToListAsync(ct);
        var tasksCreated = await db.Tasks.CountAsync(t => t.CreatedAt >= from && t.CreatedAt < to, ct);
        var tasksMissed = await db.Tasks.AsNoTracking()
            .Where(t => t.DueAt >= from && t.DueAt < to && t.DueAt < c.Now && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
            .Select(t => new { t.Id, t.Title, t.DueAt, People = t.Assignments.Where(a => a.IsPrimary).Select(a => a.User.FullName).ToList() })
            .ToListAsync(ct);
        var milestonesDone = await db.ProjectMilestones.AsNoTracking().Where(m => m.CompletedAt >= from && m.CompletedAt < to)
            .Select(m => new { m.Id, m.Title, m.CompletedAt, Project = m.Project.Name }).ToListAsync(ct);
        var recorded = await db.Episodes.AsNoTracking().Where(e => e.RecordedAt >= from && e.RecordedAt < to)
            .Select(e => new { e.Id, e.Title, e.RecordedAt, Show = e.Show.Name, e.Status }).ToListAsync(ct);
        var published = await db.Releases.AsNoTracking().Where(r => r.ActualPublishedAt >= from && r.ActualPublishedAt < to)
            .Select(r => new { r.Id, r.ActualPublishedAt, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, Show = r.Episode != null ? r.Episode.Show.Name : null }).ToListAsync(ct);
        var missedReleases = await db.Releases.AsNoTracking()
            .Where(r => r.ScheduledAt >= from && r.ScheduledAt < to && r.ScheduledAt < c.Now
                        && (r.Status == ReleaseStatus.Draft || r.Status == ReleaseStatus.AwaitingApproval || r.Status == ReleaseStatus.Ready
                            || r.Status == ReleaseStatus.Scheduled || r.Status == ReleaseStatus.Postponed))
            .Select(r => new { r.Id, r.ScheduledAt, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, Show = r.Episode != null ? r.Episode.Show.Name : null }).ToListAsync(ct);

        var rows = new List<(DateTimeOffset At, IReadOnlyList<string> Row, ReportRowLink Link)>();
        rows.AddRange(tasksDone.Select(t => (t.CompletedAt!.Value,
            (IReadOnlyList<string>)["Task completed", t.Title, c.Time(t.CompletedAt), "Completed", string.Join(", ", t.People)], Link("Task", t.Id))));
        rows.AddRange(tasksMissed.Select(t => (t.DueAt!.Value,
            (IReadOnlyList<string>)["Task missed", t.Title, c.Time(t.DueAt), "Overdue", string.Join(", ", t.People)], Link("Task", t.Id))));
        rows.AddRange(milestonesDone.Select(m => (m.CompletedAt!.Value,
            (IReadOnlyList<string>)["Milestone completed", $"{m.Title} · {m.Project}", c.Time(m.CompletedAt), "Completed", ""], Link("Milestone", m.Id))));
        rows.AddRange(recorded.Select(e => (e.RecordedAt!.Value,
            (IReadOnlyList<string>)["Episode recorded", $"{e.Show}: {e.Title}", c.Time(e.RecordedAt), e.Status.ToString(), ""], Link("Episode", e.Id))));
        rows.AddRange(published.Select(r => (r.ActualPublishedAt!.Value,
            (IReadOnlyList<string>)["Release published", Releases.ReleaseReaders.Label(r.Channel, r.Show, null, r.Title), c.Time(r.ActualPublishedAt), "Published", ""], Link("Release", r.Id))));
        rows.AddRange(missedReleases.Select(r => (r.ScheduledAt!.Value,
            (IReadOnlyList<string>)["Release missed", Releases.ReleaseReaders.Label(r.Channel, r.Show, null, r.Title), c.Time(r.ScheduledAt), "Overdue", ""], Link("Release", r.Id))));

        ReportFigure[] summary =
        [
            new("Tasks completed", tasksDone.Count.ToString()),
            new("Tasks created", tasksCreated.ToString()),
            new("Tasks missed", tasksMissed.Count.ToString()),
            new("Milestones completed", milestonesDone.Count.ToString()),
            new("Episodes recorded", recorded.Count.ToString()),
            new("Releases published", published.Count.ToString()),
            new("Releases missed", missedReleases.Count.ToString()),
        ];
        return Build(key, "Completed and missed work, recordings and publications in the period.", period, c, summary,
            ["Activity", "Item", "When", "Status", "Responsible"], rows.OrderBy(r => r.At).Select(r => (r.Row, (ReportRowLink?)r.Link)));
    }

    // ---------- releases and episodes ----------

    public async Task<ReportResponse> UpcomingReleasesAsync(int? days, string? timeZone, CancellationToken ct)
    {
        var count = days ?? 30;
        if (count is < 1 or > 180)
        {
            throw new DomainException("Upcoming releases cover between 1 and 180 days.");
        }
        var c = await ContextAsync(timeZone, ct);
        var until = c.Now.AddDays(count);
        var releases = await db.Releases.AsNoTracking()
            .Where(r => r.ScheduledAt >= c.Now && r.ScheduledAt < until && r.Status != ReleaseStatus.Published && r.Status != ReleaseStatus.Cancelled)
            .OrderBy(r => r.ScheduledAt)
            .Select(r => new
            {
                r.Id, r.ScheduledAt, r.Status, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, Show = r.Episode != null ? r.Episode.Show.Name : null,
                Assignee = db.Users.Where(u => u.Id == r.AssignedTo).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Build("upcoming-releases", "Releases scheduled from now, not yet published or cancelled.", $"Next {count} days", c,
            [new("Releases", releases.Count.ToString()), new("Channels", releases.Select(r => r.Channel).Distinct().Count().ToString()),
             new("Without an assignee", releases.Count(r => r.Assignee is null).ToString())],
            ["Scheduled", "Channel", "Show", "Episode", "Status", "Assigned to"],
            releases.Select(r => ((IReadOnlyList<string>)[c.Time(r.ScheduledAt), r.Channel, r.Show ?? "", r.Title, r.Status.ToString(), r.Assignee ?? ""],
                (ReportRowLink?)Link("Release", r.Id))));
    }

    public async Task<ReportResponse> UnpublishedEpisodesAsync(string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var episodes = await db.Episodes.AsNoTracking()
            .Where(e => e.RecordedAt != null && e.Status != EpisodeStatus.Published && e.Status != EpisodeStatus.Cancelled
                        && e.Show.Status != ShowStatus.Archived)
            .OrderBy(e => e.RecordedAt)
            .Select(e => new
            {
                e.Id, e.Title, e.RecordedAt, e.Status, Show = e.Show.Name,
                Live = db.Releases.Count(r => r.EpisodeId == e.Id && r.Status != ReleaseStatus.Cancelled),
                Published = db.Releases.Count(r => r.EpisodeId == e.Id && r.Status == ReleaseStatus.Published),
                Next = db.Releases.Where(r => r.EpisodeId == e.Id && r.ScheduledAt != null && r.Status != ReleaseStatus.Published
                                              && r.Status != ReleaseStatus.Cancelled)
                    .OrderBy(r => r.ScheduledAt).Select(r => r.ScheduledAt).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var days = (DateTimeOffset? at) => at is { } a ? ((int)(c.Now - a).TotalDays).ToString() : "";
        return Build("unpublished-episodes", "Episodes that have been recorded but are not yet published on every channel.", "Now", c,
            [new("Episodes", episodes.Count.ToString()), new("With no release planned", episodes.Count(e => e.Live == 0).ToString()),
             new("Partially published", episodes.Count(e => e.Published > 0).ToString())],
            ["Show", "Episode", "Recorded", "Days since", "Status", "Published", "Next release"],
            episodes.Select(e => ((IReadOnlyList<string>)[e.Show, e.Title, c.Time(e.RecordedAt), days(e.RecordedAt), e.Status.ToString(),
                $"{e.Published} of {e.Live}", c.Time(e.Next)], (ReportRowLink?)Link("Episode", e.Id))));
    }

    public async Task<ReportResponse> PublicationHistoryAsync(DateOnly? from, DateOnly? to, string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var today = ZonedTime.LocalDate(c.Now, c.Zone);
        var first = from ?? today.AddDays(-90);
        var last = to ?? today;
        if (last < first || last.DayNumber - first.DayNumber > MaxRangeDays)
        {
            throw new DomainException($"Choose a range that ends after it starts and covers at most {MaxRangeDays} days.");
        }
        var start = ZonedTime.StartOfDay(first, c.Zone);
        var end = ZonedTime.StartOfDay(last.AddDays(1), c.Zone);
        var releases = await db.Releases.AsNoTracking()
            .Where(r => r.Status == ReleaseStatus.Published && r.ActualPublishedAt >= start && r.ActualPublishedAt < end)
            .OrderByDescending(r => r.ActualPublishedAt)
            .Select(r => new
            {
                r.Id, r.ScheduledAt, r.ActualPublishedAt, r.ExternalUrl, Channel = r.Channel.Name, Title = r.Title ?? r.Episode!.Title, Show = r.Episode != null ? r.Episode.Show.Name : null,
                ConfirmedBy = db.Users.Where(u => u.Id == r.PublishedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        static int? Delay(DateTimeOffset? scheduled, DateTimeOffset? actual) =>
            scheduled is { } s && actual is { } a ? (int)Math.Round((a - s).TotalMinutes) : null;
        var delays = releases.Select(r => Delay(r.ScheduledAt, r.ActualPublishedAt)).OfType<int>().ToList();
        var onTime = delays.Count(d => d <= 15);
        return Build("publication-history", "Releases confirmed as published, with how their actual time compared with the schedule.",
            $"{first:d MMM yyyy} – {last:d MMM yyyy}", c,
            [new("Published", releases.Count.ToString()),
             new("On time (within 15 min)", delays.Count == 0 ? "–" : $"{onTime} ({onTime * 100 / delays.Count}%)"),
             new("Average delay (min)", delays.Count == 0 ? "–" : Math.Round(delays.Average()).ToString())],
            ["Published", "Scheduled", "Delay (min)", "Channel", "Show", "Episode", "Confirmed by", "Link"],
            releases.Select(r => ((IReadOnlyList<string>)[c.Time(r.ActualPublishedAt), c.Time(r.ScheduledAt),
                Delay(r.ScheduledAt, r.ActualPublishedAt)?.ToString() ?? "", r.Channel, r.Show ?? "", r.Title, r.ConfirmedBy ?? "", r.ExternalUrl ?? ""],
                (ReportRowLink?)Link("Release", r.Id))));
    }

    // ---------- work ----------

    public async Task<ReportResponse> OverdueTasksAsync(string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DueAt < c.Now && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
            .OrderBy(t => t.DueAt)
            .Select(t => new
            {
                t.Id, t.Title, t.DueAt, t.Priority, t.Status,
                Primary = t.Assignments.Where(a => a.IsPrimary).Select(a => a.User.FullName).FirstOrDefault(),
                Project = t.Project != null ? t.Project.Name : null,
                Episode = t.Episode != null ? t.Episode.Show.Name + ": " + t.Episode.Title : null,
            })
            .ToListAsync(ct);
        return Build("overdue-tasks", "Open tasks past their due time, oldest first.", "Now", c,
            [new("Overdue tasks", tasks.Count.ToString()), new("Urgent or high", tasks.Count(t => t.Priority is Priority.Urgent or Priority.High).ToString()),
             new("People affected", tasks.Select(t => t.Primary).Distinct().Count().ToString())],
            ["Task", "Due", "Days overdue", "Responsible", "Belongs to", "Priority", "Status"],
            tasks.Select(t => ((IReadOnlyList<string>)[t.Title, c.Time(t.DueAt), ((int)(c.Now - t.DueAt!.Value).TotalDays).ToString(), t.Primary ?? "",
                t.Project ?? t.Episode ?? "", t.Priority.ToString(), t.Status.ToString()], (ReportRowLink?)Link("Task", t.Id))));
    }

    public async Task<ReportResponse> ProjectStatusAsync(string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var today = work.Today;
        var projects = await db.Projects.AsNoTracking().Where(p => p.Status != ProjectStatus.Archived)
            .OrderBy(p => p.Status == ProjectStatus.Completed).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Status, p.TargetEndDate,
                Manager = db.Users.Where(u => u.Id == p.ManagerId).Select(u => u.FullName).FirstOrDefault(),
                OpenTasks = db.Tasks.Count(t => t.ProjectId == p.Id && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled),
                OverdueTasks = db.Tasks.Count(t => t.ProjectId == p.Id && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                                   && t.DueAt < c.Now),
                Milestones = db.ProjectMilestones.Count(m => m.ProjectId == p.Id && m.Status != WorkStatus.Cancelled),
                MilestonesDone = db.ProjectMilestones.Count(m => m.ProjectId == p.Id && m.Status == WorkStatus.Completed),
                OverdueMilestones = db.ProjectMilestones.Count(m => m.ProjectId == p.Id && m.Status != WorkStatus.Completed
                                                                    && m.Status != WorkStatus.Cancelled && m.DueDate < today),
                NextTitle = db.ProjectMilestones.Where(m => m.ProjectId == p.Id && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled)
                    .OrderBy(m => m.DueDate).Select(m => m.Title).FirstOrDefault(),
                NextDue = db.ProjectMilestones.Where(m => m.ProjectId == p.Id && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled)
                    .OrderBy(m => m.DueDate).Select(m => (DateOnly?)m.DueDate).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Build("project-status", "Every project that is not archived, with its open and overdue work.", "Now", c,
            [new("Projects", projects.Count.ToString()), new("Active", projects.Count(p => p.Status == ProjectStatus.Active).ToString()),
             new("With overdue work", projects.Count(p => p.OverdueTasks + p.OverdueMilestones > 0).ToString())],
            ["Project", "Status", "Manager", "Open tasks", "Overdue", "Milestones done", "Next milestone", "Target end"],
            projects.Select(p => ((IReadOnlyList<string>)[p.Name, p.Status.ToString(), p.Manager ?? "", p.OpenTasks.ToString(),
                (p.OverdueTasks + p.OverdueMilestones).ToString(), $"{p.MilestonesDone} of {p.Milestones}",
                p.NextTitle is null ? "" : $"{p.NextTitle} ({Context.Date(p.NextDue)})", Context.Date(p.TargetEndDate)],
                (ReportRowLink?)Link("Project", p.Id))));
    }

    public async Task<ReportResponse> TeamWorkloadAsync(string? timeZone, CancellationToken ct)
    {
        var c = await ContextAsync(timeZone, ct);
        var people = await workload.TeamAsync(ct);
        var ids = people.Select(p => p.UserId).ToList();
        var open = db.Episodes.AsNoTracking().Where(e => e.Status != EpisodeStatus.Published && e.Status != EpisodeStatus.Cancelled);
        var producing = await open.Where(e => e.ProducerId != null && ids.Contains(e.ProducerId.Value))
            .Select(e => new { EpisodeId = e.Id, UserId = e.ProducerId!.Value }).ToListAsync(ct);
        var crewing = await open.SelectMany(e => e.Assignments).Where(a => ids.Contains(a.UserId))
            .Select(a => new { a.EpisodeId, a.UserId }).ToListAsync(ct);
        var episodes = producing.Concat(crewing).Distinct().GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Count());
        var releases = await db.Releases.AsNoTracking()
            .Where(r => r.AssignedTo != null && r.Status != ReleaseStatus.Published && r.Status != ReleaseStatus.Cancelled)
            .GroupBy(r => r.AssignedTo!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return Build("team-workload", "Open work per active person, most pressing first.", "Now", c,
            [new("People", people.Count.ToString()), new("Open tasks", people.Sum(p => p.OpenTasks).ToString()),
             new("Overdue tasks", people.Sum(p => p.OverdueTasks).ToString())],
            ["Person", "Open tasks", "Overdue", "Due this week", "Open milestones", "Open episodes", "Open releases"],
            people.Select(p => ((IReadOnlyList<string>)[p.FullName, p.OpenTasks.ToString(), p.OverdueTasks.ToString(), p.DueThisWeek.ToString(),
                p.OpenMilestones.ToString(), episodes.GetValueOrDefault(p.UserId).ToString(), releases.GetValueOrDefault(p.UserId).ToString()],
                (ReportRowLink?)null)));
    }
}
