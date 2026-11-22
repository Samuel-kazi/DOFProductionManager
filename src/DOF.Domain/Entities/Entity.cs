namespace DOF.Domain.Entities;

/// <summary>
/// Base type for every persisted record. Identifiers are UUIDv7 (time-ordered) so primary-key
/// indexes stay compact. Timestamps are UTC and are maintained by the persistence layer.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
