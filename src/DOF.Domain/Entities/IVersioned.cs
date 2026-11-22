namespace DOF.Domain.Entities;

/// <summary>
/// Marks an important editable record that uses an integer <c>version</c> column for optimistic
/// concurrency. The persistence layer increments the version on every update and rejects saves
/// made against an outdated version.
/// </summary>
public interface IVersioned
{
    int Version { get; }
}
