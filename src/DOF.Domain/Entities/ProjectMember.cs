namespace DOF.Domain.Entities;

public sealed class ProjectMember : Entity
{
    private ProjectMember() { }

    public ProjectMember(Guid projectId, Guid userId, string projectRole, DateTimeOffset joinedAt)
    {
        ProjectId = projectId;
        UserId = userId;
        ProjectRole = projectRole;
        JoinedAt = joinedAt;
    }

    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public string ProjectRole { get; private set; } = null!;
    public DateTimeOffset JoinedAt { get; private set; }
    public User User { get; private set; } = null!;

    internal void ChangeRole(string role) => ProjectRole = role;
}
