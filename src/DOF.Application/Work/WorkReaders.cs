using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Work;

/// <summary>Loads tasks and milestones as responses, resolving names and permissions in batches.</summary>
public sealed class WorkReaders(
    IApplicationDbContext db,
    WorkAccess access,
    IOptions<OrganisationOptions> organisation,
    TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Today in the organisation's timezone (milestone due dates are calendar dates).</summary>
    public DateOnly Today => ZonedTime.LocalDate(Now, ZonedTime.Find(organisation.Value.DefaultTimeZone));

    public async Task<List<TaskResponse>> TasksAsync(IQueryable<WorkTask> query, CancellationToken ct)
    {
        var tasks = await query.AsNoTracking()
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .Include(t => t.Project)
            .ToListAsync(ct);

        var names = await NamesAsync(tasks.SelectMany(t => new[] { t.CreatedBy, t.CompletedBy }), ct);
        var now = Now;
        return tasks.Select(t => new TaskResponse(
            t.Id, t.Title, t.Description,
            t.Project is null ? null : new ProjectRefResponse(t.Project.Id, t.Project.Name),
            t.DueAt, t.Priority.ToString(), t.Status.ToString(),
            [.. t.Assignments.OrderByDescending(a => a.IsPrimary).ThenBy(a => a.User.FullName)
                .Select(a => new TaskAssigneeResponse(a.UserId, a.User.FullName, a.IsPrimary))],
            Summary(t.CreatedBy, names), t.CompletedAt, Summary(t.CompletedBy, names),
            t.IsOverdue(now), t.CreatedAt, t.UpdatedAt, t.Version,
            access.CanEditTask(t.CreatedBy, t.Assignments.Select(a => a.UserId)))).ToList();
    }

    public async Task<TaskResponse?> TaskAsync(Guid id, CancellationToken ct) =>
        (await TasksAsync(db.Tasks.Where(t => t.Id == id), ct)).SingleOrDefault();

    public async Task<List<MilestoneResponse>> MilestonesAsync(IQueryable<ProjectMilestone> query, CancellationToken ct)
    {
        var milestones = await query.AsNoTracking().Include(m => m.Project).ToListAsync(ct);
        var names = await NamesAsync(milestones.Select(m => m.AssignedTo), ct);
        var today = Today;
        return milestones.Select(m => new MilestoneResponse(
            m.Id, new ProjectRefResponse(m.ProjectId, m.Project.Name), m.Title, m.Description, m.DueDate,
            m.Status.ToString(), m.Priority.ToString(), Summary(m.AssignedTo, names), m.CompletedAt,
            m.IsOverdue(today), m.Version, access.CanEditMilestone(m.Project.ManagerId, m.AssignedTo))).ToList();
    }

    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public static UserSummaryResponse? Summary(Guid? id, IReadOnlyDictionary<Guid, string> names) =>
        id is { } value && names.TryGetValue(value, out var name) ? new UserSummaryResponse(value, name) : null;

    /// <summary>Open first by due date (undated last); finished work by completion, newest first.</summary>
    public static IQueryable<WorkTask> Ordered(IQueryable<WorkTask> query) =>
        query.OrderBy(t => t.Status == Domain.Enums.WorkStatus.Completed || t.Status == Domain.Enums.WorkStatus.Cancelled)
            .ThenBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenByDescending(t => t.CompletedAt)
            .ThenBy(t => t.Id);
}
