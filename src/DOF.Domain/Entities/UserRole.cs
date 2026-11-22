namespace DOF.Domain.Entities;

public sealed class UserRole : Entity
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Role Role { get; private set; } = null!;
}
