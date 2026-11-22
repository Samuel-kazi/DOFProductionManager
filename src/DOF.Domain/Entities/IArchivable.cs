namespace DOF.Domain.Entities;

/// <summary>
/// Operational records are archived rather than permanently deleted.
/// </summary>
public interface IArchivable
{
    DateTimeOffset? ArchivedAt { get; }
}
