using DOF.Application.Common.Exceptions;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Work;

/// <summary>Who is carrying what: open, overdue and soon-due work per person.</summary>
public sealed class WorkloadService(IApplicationDbContext db, WorkReaders readers)
{
    public async Task<IReadOnlyList<WorkloadSummaryResponse>> TeamAsync(CancellationToken ct)
    {
        var now = readers.Now;
        var weekAhead = now.AddDays(7);
        var rows = await db.Users.AsNoTracking().Where(u => u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                Open = db.Tasks.Count(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                           && t.Assignments.Any(a => a.UserId == u.Id)),
                Overdue = db.Tasks.Count(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                              && t.DueAt != null && t.DueAt < now && t.Assignments.Any(a => a.UserId == u.Id)),
                DueSoon = db.Tasks.Count(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                              && t.DueAt != null && t.DueAt >= now && t.DueAt < weekAhead
                                              && t.Assignments.Any(a => a.UserId == u.Id)),
                Milestones = db.ProjectMilestones.Count(m => m.AssignedTo == u.Id && m.Status != WorkStatus.Completed
                                                             && m.Status != WorkStatus.Cancelled),
            })
            .ToListAsync(ct);

        // Most pressing first: overdue, then open load. Sorted in memory (EF cannot order by a constructed record).
        return [.. rows
            .Select(r => new WorkloadSummaryResponse(r.Id, r.FullName, r.Open, r.Overdue, r.DueSoon, r.Milestones))
            .OrderByDescending(w => w.OverdueTasks).ThenByDescending(w => w.OpenTasks).ThenBy(w => w.FullName)];
    }

    public async Task<WorkloadResponse> ForUserAsync(Guid userId, CancellationToken ct)
    {
        var summary = (await TeamAsync(ct)).SingleOrDefault(w => w.UserId == userId);
        if (summary is null)
        {
            var name = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).SingleOrDefaultAsync(ct)
                       ?? throw new NotFoundException("User", userId);
            summary = new WorkloadSummaryResponse(userId, name, 0, 0, 0, 0); // deactivated users carry no current load
        }

        var tasks = await readers.TasksAsync(WorkReaders.Ordered(db.Tasks.Where(t =>
            t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled && t.Assignments.Any(a => a.UserId == userId))), ct);
        var milestones = await readers.MilestonesAsync(db.ProjectMilestones
            .Where(m => m.AssignedTo == userId && m.Status != WorkStatus.Completed && m.Status != WorkStatus.Cancelled)
            .OrderBy(m => m.DueDate), ct);
        return new WorkloadResponse(summary, tasks, milestones);
    }
}
