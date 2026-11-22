using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>A dated checkpoint in a project. Its due date appears on the calendar without being copied.</summary>
public sealed class ProjectMilestone : AuditableEntity, IVersioned
{
    private ProjectMilestone() { }

    public Guid ProjectId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateOnly DueDate { get; private set; }
    public WorkStatus Status { get; private set; }
    public Priority Priority { get; private set; }
    public Guid? AssignedTo { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int Version { get; private set; }
    public Project Project { get; private set; } = null!;

    public static ProjectMilestone Create(Guid projectId, string title, string? description, DateOnly dueDate,
        Priority priority, Guid? assignedTo)
    {
        var milestone = new ProjectMilestone { ProjectId = projectId, Status = WorkStatus.NotStarted };
        milestone.Update(title, description, dueDate, priority, assignedTo);
        return milestone;
    }

    public void Update(string title, string? description, DateOnly dueDate, Priority priority, Guid? assignedTo)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("A milestone needs a title.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DueDate = dueDate;
        Priority = priority;
        AssignedTo = assignedTo;
    }

    public void Reschedule(DateOnly dueDate) => DueDate = dueDate;

    public void ChangeStatus(WorkStatus status, DateTimeOffset now)
    {
        CompletedAt = status == WorkStatus.Completed ? CompletedAt ?? now : null;
        Status = status;
    }

    public bool IsOpen => Status is not (WorkStatus.Completed or WorkStatus.Cancelled);

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate < today;
}
