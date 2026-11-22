using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// A unit of work with one primary responsible person and optional helpers. It may stand alone or
/// belong to a project. (Named WorkTask to avoid confusion with System.Threading.Tasks.Task.)
/// </summary>
public sealed class WorkTask : AuditableEntity, IVersioned
{
    private readonly List<TaskAssignment> _assignments = [];

    private WorkTask() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public Priority Priority { get; private set; }
    public WorkStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public int Version { get; private set; }
    public Project? Project { get; private set; }

    public IReadOnlyCollection<TaskAssignment> Assignments => _assignments;

    public Guid? PrimaryAssigneeId => _assignments.FirstOrDefault(a => a.IsPrimary)?.UserId;

    public bool IsOpen => Status is not (WorkStatus.Completed or WorkStatus.Cancelled);

    public bool IsOverdue(DateTimeOffset now) => IsOpen && DueAt is { } due && due < now;

    public static WorkTask Create(string title, string? description, Guid? projectId, DateTimeOffset? dueAt, Priority priority)
    {
        var task = new WorkTask { Status = WorkStatus.NotStarted };
        task.Update(title, description, projectId, dueAt, priority);
        return task;
    }

    public void Update(string title, string? description, Guid? projectId, DateTimeOffset? dueAt, Priority priority)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("A task needs a title.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ProjectId = projectId;
        DueAt = dueAt?.ToUniversalTime();
        Priority = priority;
    }

    public void Reschedule(DateTimeOffset? dueAt) => DueAt = dueAt?.ToUniversalTime();

    /// <summary>Records who completed the task and when; reopening clears both (history keeps them).</summary>
    public void ChangeStatus(WorkStatus status, Guid? changedBy, DateTimeOffset now)
    {
        if (status == WorkStatus.Completed)
        {
            if (Status != WorkStatus.Completed)
            {
                CompletedAt = now;
                CompletedBy = changedBy;
            }
        }
        else
        {
            CompletedAt = null;
            CompletedBy = null;
        }
        Status = status;
    }

    /// <summary>
    /// Replaces the assignees. Every task has exactly one primary responsible person, who must be among
    /// the assignees; if none is named, the first assignee becomes primary.
    /// </summary>
    public void SetAssignees(IReadOnlyList<Guid> userIds, Guid? primaryId, DateTimeOffset now)
    {
        var wanted = userIds.Distinct().ToList();
        if (primaryId is { } p && !wanted.Contains(p))
        {
            wanted.Insert(0, p);
        }
        if (wanted.Count == 0)
        {
            throw new DomainException("A task needs at least one responsible person.");
        }

        var primary = primaryId ?? wanted[0];
        _assignments.RemoveAll(a => !wanted.Contains(a.UserId));
        foreach (var userId in wanted.Where(id => _assignments.All(a => a.UserId != id)))
        {
            _assignments.Add(new TaskAssignment(Id, userId, now, isPrimary: false));
        }
        foreach (var assignment in _assignments)
        {
            assignment.SetPrimary(assignment.UserId == primary);
        }
    }

    /// <summary>Adds one assignee (or makes an existing one primary).</summary>
    public void Assign(Guid userId, bool isPrimary, DateTimeOffset now)
    {
        var ids = _assignments.Select(a => a.UserId).Append(userId).ToList();
        SetAssignees(ids, isPrimary ? userId : PrimaryAssigneeId, now);
    }

    public void Unassign(Guid userId, DateTimeOffset now)
    {
        var remaining = _assignments.Where(a => a.UserId != userId).Select(a => a.UserId).ToList();
        if (remaining.Count == 0)
        {
            throw new DomainException("A task needs at least one responsible person.");
        }
        SetAssignees(remaining, PrimaryAssigneeId == userId ? null : PrimaryAssigneeId, now);
    }
}
