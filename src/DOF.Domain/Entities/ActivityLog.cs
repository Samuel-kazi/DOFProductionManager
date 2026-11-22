namespace DOF.Domain.Entities;

/// <summary>
/// One recorded change to an important record. Append-only: the application never updates or deletes
/// entries, and the database rejects attempts to (trigger in the migration).
/// </summary>
public sealed class ActivityLog : Entity
{
    private ActivityLog() { }

    public ActivityLog(Guid? userId, string entityType, Guid entityId, string action, string? oldValues, string? newValues)
    {
        UserId = userId;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
    }

    public Guid? UserId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = null!;
    /// <summary>JSON object of the changed fields before the change.</summary>
    public string? OldValues { get; private set; }
    /// <summary>JSON object of the changed fields after the change.</summary>
    public string? NewValues { get; private set; }
}
