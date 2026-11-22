using DOF.Application.Calendar;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Releases;
using DOF.Application.Shows;
using DOF.Application.Work;
using DOF.Contracts.Responses;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Dashboard;

/// <summary>
/// Dashboards built from the aggregated calendar and live work records. Every entry links to its
/// original record; counts are only shown where they lead to a list (workload, overdue).
/// </summary>
public sealed class DashboardService(
    CalendarService calendar,
    ICurrentUser currentUser,
    IApplicationDbContext db,
    WorkReaders readers,
    ProjectService projects,
    WorkloadService workload,
    EpisodeService episodes,
    ShowReaders showReaders,
    ReleaseService releases)
{
    public const int DefaultDays = 7;
    public const int MaxDays = 60;
    public const int MilestoneWindowDays = 14;
    public const int ReleaseWindowDays = 14;
    public const int ListLimit = 25;

    public async Task<PersonalDashboardResponse> GetPersonalAsync(string? timeZone, CancellationToken ct)
    {
        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var today = calendar.Today(zone);
        var me = currentUser.RequiredUserId;

        var todayStart = ZonedTime.StartOfDay(today, zone);
        var tomorrowStart = ZonedTime.StartOfDay(today.AddDays(1), zone);
        var todaySchedule = await calendar.GetItemsAsync(new CalendarQuery(todayStart, tomorrowStart, AssignedUserId: me), ct);
        var upcoming = await calendar.GetItemsAsync(new CalendarQuery(
            tomorrowStart, ZonedTime.StartOfDay(today.AddDays(1 + DefaultDays), zone), AssignedUserId: me), ct);

        var myTasks = await readers.TasksAsync(WorkReaders.Ordered(db.Tasks.Where(t =>
                t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled && t.Assignments.Any(a => a.UserId == me)))
            .Take(ListLimit), ct);
        var pendingReviews = await readers.TasksAsync(WorkReaders.Ordered(db.Tasks.Where(t =>
                t.Status == WorkStatus.AwaitingReview && t.CreatedBy == me))
            .Take(ListLimit), ct);

        return new PersonalDashboardResponse(today, zone.Id, todaySchedule, upcoming, myTasks, pendingReviews,
            await episodes.MineAsync(me, ListLimit, ct));
    }

    public async Task<ManagementDashboardResponse> GetManagementAsync(
        DateOnly? start, DateOnly? end, string? timeZone, CancellationToken ct)
    {
        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var (from, to) = Range(start, end, zone);
        var items = await calendar.GetItemsAsync(new CalendarQuery(from, to), ct);

        var activeProjects = await projects.Summaries(db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Active).OrderBy(p => p.Name)).Take(ListLimit).ToListAsync(ct);
        var today = readers.Today;
        var upcomingMilestones = await readers.MilestonesAsync(db.ProjectMilestones.Where(m =>
                m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled
                && m.DueDate >= today && m.DueDate <= today.AddDays(MilestoneWindowDays)
                && m.Project.Status != ProjectStatus.Archived)
            .OrderBy(m => m.DueDate).Take(ListLimit), ct);

        return new ManagementDashboardResponse(from, to, zone.Id, items, [.. items.Where(i => i.AssignedUsers.Count == 0)],
            activeProjects, upcomingMilestones, await GetOverdueAsync(mine: false, ct), await workload.TeamAsync(ct),
            await AwaitingPublicationAsync(ct),
            await releases.UpcomingAsync(ZonedTime.StartOfDay(calendar.Today(zone).AddDays(ReleaseWindowDays + 1), zone), ListLimit, ct));
    }

    /// <summary>Approved or ready episodes not yet fully published, oldest recording first.</summary>
    public async Task<IReadOnlyList<EpisodeSummaryResponse>> AwaitingPublicationAsync(CancellationToken ct)
    {
        var items = await showReaders.EpisodesAsync(db.Episodes.Where(e =>
            (e.Status == EpisodeStatus.Approved || e.Status == EpisodeStatus.ReadyForRelease
             || e.Status == EpisodeStatus.PartiallyPublished) && e.Show.Status != ShowStatus.Archived), ct);
        return [.. items.OrderBy(e => e.RecordingStart ?? DateTimeOffset.MaxValue).Take(ListLimit)];
    }

    /// <summary>Open tasks past their due time and open milestones past their due date.</summary>
    public async Task<OverdueResponse> GetOverdueAsync(bool mine, CancellationToken ct)
    {
        var now = readers.Now;
        var today = readers.Today;
        var me = currentUser.RequiredUserId;

        var tasks = db.Tasks.Where(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                        && t.DueAt != null && t.DueAt < now);
        var milestones = db.ProjectMilestones.Where(m => m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled
                                                         && m.DueDate < today && m.Project.Status != ProjectStatus.Archived);
        // Candidates are narrowed in SQL; Episode.IsOverdue applies the stage rules.
        var episodes = db.Episodes.Where(e => e.Status != EpisodeStatus.Cancelled && e.Status != EpisodeStatus.Published
                                              && e.Show.Status != ShowStatus.Archived
                                              && (e.EditingDueDate < today || e.ReviewDueDate < today || e.ApprovalDueDate < today
                                                  || (e.RecordingEnd < now && (e.Status == EpisodeStatus.Planned
                                                                               || e.Status == EpisodeStatus.Scheduled))));
        if (mine)
        {
            tasks = tasks.Where(t => t.Assignments.Any(a => a.UserId == me));
            milestones = milestones.Where(m => m.AssignedTo == me);
            episodes = episodes.Where(e => e.ProducerId == me || e.Assignments.Any(a => a.UserId == me));
        }

        var overdueEpisodes = (await showReaders.EpisodesAsync(episodes, ct)).Where(e => e.IsOverdue)
            .OrderBy(e => e.NextDeadline ?? DateOnly.MaxValue).Take(100).ToList();
        return new OverdueResponse(
            await readers.TasksAsync(tasks.OrderBy(t => t.DueAt).Take(100), ct),
            await readers.MilestonesAsync(milestones.OrderBy(m => m.DueDate).Take(100), ct),
            overdueEpisodes,
            await releases.OverdueAsync(mine ? me : null, 100, ct));
    }

    public async Task<CalendarRangeResponse> GetUpcomingAsync(int? days, bool mine, string? timeZone, CancellationToken ct)
    {
        var count = days ?? DefaultDays;
        if (count is < 1 or > MaxDays)
        {
            throw new DomainException($"Upcoming covers between 1 and {MaxDays} days.");
        }

        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var today = calendar.Today(zone);
        var from = ZonedTime.StartOfDay(today, zone);
        var to = ZonedTime.StartOfDay(today.AddDays(count), zone);
        var items = await calendar.GetItemsAsync(
            new CalendarQuery(from, to, AssignedUserId: mine ? currentUser.RequiredUserId : null), ct);
        return new CalendarRangeResponse(from, to, zone.Id, items);
    }

    private (DateTimeOffset From, DateTimeOffset To) Range(DateOnly? start, DateOnly? end, TimeZoneInfo zone)
    {
        var first = start ?? calendar.Today(zone);
        var last = end ?? first.AddDays(DefaultDays - 1);
        if (last < first)
        {
            throw new DomainException("The end date must not be before the start date.");
        }
        if (last.DayNumber - first.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"Dashboards cover at most {MaxDays} days.");
        }

        return (ZonedTime.StartOfDay(first, zone), ZonedTime.StartOfDay(last.AddDays(1), zone));
    }
}
