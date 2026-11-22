namespace DOF.Domain.Entities;

public sealed class TaskAssignment : Entity
{
    private TaskAssignment() { }

    public TaskAssignment(Guid taskId, Guid userId, DateTimeOffset assignedAt, bool isPrimary)
    {
        TaskId = taskId;
        UserId = userId;
        AssignedAt = assignedAt;
        IsPrimary = isPrimary;
    }

    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public bool IsPrimary { get; private set; }
    public User User { get; private set; } = null!;

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
