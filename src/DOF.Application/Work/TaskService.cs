using DOF.Application.Activity;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Work;

/// <summary>Filters for task lists. All optional.</summary>
public sealed record TaskQuery(
    Guid? AssigneeId = null,
    Guid? CreatedById = null,
    bool AssignedByMe = false,
    Guid? ProjectId = null,
    Guid? EpisodeId = null,
    IReadOnlyCollection<string>? Statuses = null,
    bool? Open = null,
    bool OverdueOnly = false,
    string? Search = null);

public sealed class TaskService(
    IApplicationDbContext db,
    WorkAccess access,
    WorkReaders readers,
    ActivityRecorder activity,
    ActivityQueries history,
    ILogger<TaskService> logger)
{
    public async Task<PagedResponse<TaskResponse>> ListAsync(TaskQuery filter, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = Apply(db.Tasks.AsNoTracking(), filter);
        var total = await query.CountAsync(ct);
        var ids = await WorkReaders.Ordered(query).Skip((p - 1) * size).Take(size).Select(t => t.Id).ToListAsync(ct);
        var items = (await readers.TasksAsync(db.Tasks.Where(t => ids.Contains(t.Id)), ct)).ToDictionary(t => t.Id);
        return new PagedResponse<TaskResponse>([.. ids.Select(id => items[id])], p, size, total);
    }

    public Task<PagedResponse<TaskResponse>> MyTasksAsync(int? page, int? pageSize, CancellationToken ct) =>
        ListAsync(new TaskQuery(AssigneeId: access.Me, Open: true), page, pageSize, ct);

    public Task<PagedResponse<TaskResponse>> OverdueAsync(bool mine, int? page, int? pageSize, CancellationToken ct) =>
        ListAsync(new TaskQuery(AssigneeId: mine ? access.Me : null, OverdueOnly: true), page, pageSize, ct);

    public async Task<TaskResponse> GetAsync(Guid id, CancellationToken ct) =>
        await readers.TaskAsync(id, ct) ?? throw new NotFoundException("Task", id);

    public async Task<IReadOnlyList<ActivityEntryResponse>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Tasks.AnyAsync(t => t.Id == id, ct))
        {
            throw new NotFoundException("Task", id);
        }
        return await history.ForEntityAsync(ActivityEntities.Task, id, ct);
    }

    public async Task<TaskResponse> CreateAsync(CreateTaskRequest request, CancellationToken ct)
    {
        var me = access.Me;
        var assignees = request.AssigneeIds is { Count: > 0 } ids ? ids.ToList() : [me];
        if (request.PrimaryAssigneeId is { } primary && !assignees.Contains(primary))
        {
            assignees.Insert(0, primary);
        }
        EnsureMayAssign(assignees, alreadyAssigned: []);
        await EnsureProjectOpenAsync(request.ProjectId, ct);
        await EnsureEpisodeOpenAsync(request.EpisodeId, ct);
        await EnsureActiveUsersAsync(assignees, ct);

        var task = WorkTask.Create(request.Title, request.Description, request.ProjectId, request.EpisodeId, request.DueAt,
            WorkParsing.Parse<Priority>(request.Priority, "priority"));
        task.SetAssignees(assignees, request.PrimaryAssigneeId, readers.Now);
        db.Tasks.Add(task);

        activity.Record(ActivityEntities.Task, task.Id, ActivityActions.Created, null,
            new { task.Title, task.DueAt, Assignees = await AssigneeNamesAsync(task, ct) });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} created by {UserId}", task.Id, me);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskResponse> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await LoadAsync(id, request.Version, ct);
        EnsureCanEdit(task);
        var current = task.Assignments.Select(a => a.UserId).ToList();
        EnsureMayAssign(request.AssigneeIds, current);
        if (request.ProjectId != task.ProjectId)
        {
            await EnsureProjectOpenAsync(request.ProjectId, ct);
        }
        if (request.EpisodeId != task.EpisodeId)
        {
            await EnsureEpisodeOpenAsync(request.EpisodeId, ct);
        }
        await EnsureActiveUsersAsync(request.AssigneeIds.Except(current), ct);

        var before = Snapshot(task);
        var previousDue = task.DueAt;
        var previousAssignees = await AssigneeNamesAsync(task, ct);
        var previousPrimary = task.PrimaryAssigneeId;

        task.Update(request.Title, request.Description, request.ProjectId, request.EpisodeId, request.DueAt,
            WorkParsing.Parse<Priority>(request.Priority, "priority"));
        task.SetAssignees(request.AssigneeIds, request.PrimaryAssigneeId, readers.Now);

        if (previousDue != task.DueAt)
        {
            activity.Record(ActivityEntities.Task, id, ActivityActions.Rescheduled, new { DueAt = previousDue }, new { DueAt = task.DueAt });
        }
        var assigneesNow = await AssigneeNamesAsync(task, ct);
        if (!previousAssignees.SequenceEqual(assigneesNow) || previousPrimary != task.PrimaryAssigneeId)
        {
            activity.Record(ActivityEntities.Task, id, ActivityActions.AssigneesChanged,
                new { Assignees = previousAssignees }, new { Assignees = assigneesNow });
        }
        activity.RecordChanges(ActivityEntities.Task, id, ActivityActions.Updated, before, Snapshot(task));

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskResponse> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct)
    {
        var task = await LoadAsync(id, request.Version, ct);
        EnsureCanEdit(task);
        var status = WorkParsing.Parse<WorkStatus>(request.Status, "status");
        if (status != task.Status)
        {
            var previous = task.Status;
            task.ChangeStatus(status, access.Me, readers.Now);
            activity.Record(ActivityEntities.Task, id, ActivityActions.StatusChanged,
                new { Status = previous.ToString() }, new { Status = status.ToString() });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Task {TaskId} status {From} -> {To} by {UserId}", id, previous, status, access.Me);
        }
        return await GetAsync(id, ct);
    }

    public async Task<TaskResponse> AssignAsync(Guid id, AssignTaskRequest request, CancellationToken ct)
    {
        var task = await LoadAsync(id, null, ct);
        EnsureCanEdit(task);
        var current = task.Assignments.Select(a => a.UserId).ToList();
        EnsureMayAssign([request.UserId], current);
        await EnsureActiveUsersAsync([request.UserId], ct);

        var before = await AssigneeNamesAsync(task, ct);
        task.Assign(request.UserId, request.IsPrimary, readers.Now);
        await RecordAssigneesAsync(task, before, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskResponse> UnassignAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var task = await LoadAsync(id, null, ct);
        EnsureCanEdit(task);
        if (userId != access.Me && !access.CanAssignOthers)
        {
            throw new ForbiddenException("Changing who else is responsible needs the tasks.assign permission.");
        }

        var before = await AssigneeNamesAsync(task, ct);
        task.Unassign(userId, readers.Now);
        await RecordAssigneesAsync(task, before, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Moves only the due date (used by calendar drag-and-drop).</summary>
    public async Task<TaskResponse> RescheduleAsync(Guid id, DateTimeOffset? dueAt, int version, CancellationToken ct)
    {
        var task = await LoadAsync(id, version, ct);
        EnsureCanEdit(task);
        var previous = task.DueAt;
        task.Reschedule(dueAt);
        if (previous != task.DueAt)
        {
            activity.Record(ActivityEntities.Task, id, ActivityActions.Rescheduled, new { DueAt = previous }, new { DueAt = task.DueAt });
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    // ---------- helpers ----------

    internal IQueryable<WorkTask> Apply(IQueryable<WorkTask> query, TaskQuery filter)
    {
        if (filter.AssigneeId is { } assignee)
        {
            query = query.Where(t => t.Assignments.Any(a => a.UserId == assignee));
        }
        if (filter.CreatedById is { } creator)
        {
            query = query.Where(t => t.CreatedBy == creator);
        }
        if (filter.AssignedByMe)
        {
            var me = access.Me;
            query = query.Where(t => t.CreatedBy == me && t.Assignments.Any(a => a.UserId != me));
        }
        if (filter.ProjectId is { } project)
        {
            query = query.Where(t => t.ProjectId == project);
        }
        if (filter.EpisodeId is { } episode)
        {
            query = query.Where(t => t.EpisodeId == episode);
        }
        if (WorkParsing.ParseAll<WorkStatus>(filter.Statuses) is { } statuses)
        {
            query = query.Where(t => statuses.Contains(t.Status));
        }
        if (filter.Open is { } open)
        {
            query = open
                ? query.Where(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
                : query.Where(t => t.Status == WorkStatus.Completed || t.Status == WorkStatus.Cancelled);
        }
        if (filter.OverdueOnly)
        {
            var now = readers.Now;
            query = query.Where(t => t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled
                                     && t.DueAt != null && t.DueAt < now);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Title.ToLower().Contains(term));
        }
        return query;
    }

    private async Task<WorkTask> LoadAsync(Guid id, int? version, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.Assignments).SingleOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new NotFoundException("Task", id);
        if (version is { } expected)
        {
            if (task.Version != expected)
            {
                throw new ConflictException("This task was modified by someone else. Reload the latest version and try again.");
            }
            db.Entry(task).Property(t => t.Version).OriginalValue = expected;
        }
        return task;
    }

    private void EnsureCanEdit(WorkTask task)
    {
        if (!access.CanEditTask(task.CreatedBy, task.Assignments.Select(a => a.UserId)))
        {
            throw new ForbiddenException();
        }
    }

    /// <summary>Anyone may take work on themselves; giving it to someone new needs <c>tasks.assign</c>.</summary>
    private void EnsureMayAssign(IEnumerable<Guid> wanted, IReadOnlyCollection<Guid> alreadyAssigned)
    {
        if (!access.CanAssignOthers && wanted.Any(id => id != access.Me && !alreadyAssigned.Contains(id)))
        {
            throw new ForbiddenException("Assigning tasks to other people needs the tasks.assign permission.");
        }
    }

    private async Task EnsureProjectOpenAsync(Guid? projectId, CancellationToken ct)
    {
        if (projectId is null)
        {
            return;
        }
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, ct)
                      ?? throw new DomainException("The selected project does not exist.");
        project.EnsureNotArchived();
    }

    private async Task EnsureEpisodeOpenAsync(Guid? episodeId, CancellationToken ct)
    {
        if (episodeId is null)
        {
            return;
        }
        var episode = await db.Episodes.Include(e => e.Show).SingleOrDefaultAsync(e => e.Id == episodeId, ct)
                      ?? throw new DomainException("The selected episode does not exist.");
        episode.Show.EnsureNotArchived();
        if (episode.IsCancelled)
        {
            throw new DomainException("The selected episode is cancelled.");
        }
    }

    private async Task EnsureActiveUsersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count > 0 && await db.Users.CountAsync(u => wanted.Contains(u.Id) && u.IsActive, ct) != wanted.Count)
        {
            throw new DomainException("Tasks can only be assigned to existing, active users.");
        }
    }

    private async Task<List<string>> AssigneeNamesAsync(WorkTask task, CancellationToken ct)
    {
        var names = await readers.NamesAsync(task.Assignments.Select(a => (Guid?)a.UserId), ct);
        return [.. task.Assignments.OrderByDescending(a => a.IsPrimary).ThenBy(a => names.GetValueOrDefault(a.UserId))
            .Select(a => a.IsPrimary ? $"{names.GetValueOrDefault(a.UserId)} (primary)" : names.GetValueOrDefault(a.UserId) ?? "?")];
    }

    private async Task RecordAssigneesAsync(WorkTask task, List<string> before, CancellationToken ct)
    {
        var after = await AssigneeNamesAsync(task, ct);
        if (!before.SequenceEqual(after))
        {
            activity.Record(ActivityEntities.Task, task.Id, ActivityActions.AssigneesChanged, new { Assignees = before }, new { Assignees = after });
        }
    }

    private static Dictionary<string, object?> Snapshot(WorkTask t) => new()
    {
        ["Title"] = t.Title, ["Description"] = t.Description, ["Priority"] = t.Priority.ToString(), ["Project"] = t.ProjectId,
        ["Episode"] = t.EpisodeId,
    };
}
