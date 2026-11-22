using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>A programme made in seasons and episodes (or as a one-off production).</summary>
public sealed class Show : AuditableEntity, IVersioned, IArchivable
{
    public const int MaxDurationMinutes = 24 * 60;

    private Show() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? ProducerId { get; private set; }
    public ShowType ShowType { get; private set; }
    public ShowStatus Status { get; private set; }
    public int? DefaultDurationMinutes { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public int Version { get; private set; }

    public bool IsArchived => Status == ShowStatus.Archived;

    public static Show Create(string name, string? description, ShowType type, Guid? producerId, int? defaultDurationMinutes)
    {
        var show = new Show { Status = ShowStatus.InDevelopment };
        show.Update(name, description, type, producerId, defaultDurationMinutes);
        return show;
    }

    public void Update(string name, string? description, ShowType type, Guid? producerId, int? defaultDurationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A show needs a name.");
        }
        if (defaultDurationMinutes is { } minutes && (minutes < 1 || minutes > MaxDurationMinutes))
        {
            throw new DomainException("The default duration must be between 1 minute and 24 hours.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ShowType = type;
        ProducerId = producerId;
        DefaultDurationMinutes = defaultDurationMinutes;
    }

    public void ChangeStatus(ShowStatus status, DateTimeOffset now)
    {
        ArchivedAt = status == ShowStatus.Archived ? ArchivedAt ?? now : null;
        Status = status;
    }

    public void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException("This show is archived. Restore it before adding or changing its seasons and episodes.");
        }
    }
}
