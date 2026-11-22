using DOF.Domain.Enums;

namespace DOF.Domain.Entities;

/// <summary>A person's responsibility on one episode. One person may hold several responsibilities.</summary>
public sealed class EpisodeAssignment : Entity
{
    private EpisodeAssignment() { }

    public EpisodeAssignment(Guid episodeId, Guid userId, EpisodeResponsibility responsibility, DateTimeOffset assignedAt)
    {
        EpisodeId = episodeId;
        UserId = userId;
        Responsibility = responsibility;
        AssignedAt = assignedAt;
    }

    public Guid EpisodeId { get; private set; }
    public Guid UserId { get; private set; }
    public EpisodeResponsibility Responsibility { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public User User { get; private set; } = null!;
}
