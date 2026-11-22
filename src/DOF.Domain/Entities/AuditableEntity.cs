namespace DOF.Domain.Entities;

/// <summary>An entity that records who created and last changed it (set by the persistence layer).</summary>
public abstract class AuditableEntity : Entity
{
    public Guid? CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }
}
