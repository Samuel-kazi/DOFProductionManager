namespace DOF.Domain.Entities;

public sealed class Role : Entity
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    public Role(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public void Grant(Guid permissionId)
    {
        if (_permissions.All(p => p.PermissionId != permissionId))
        {
            _permissions.Add(new RolePermission(Id, permissionId));
        }
    }

    public void SetPermissions(IEnumerable<Guid> permissionIds)
    {
        var wanted = permissionIds.ToHashSet();
        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionId));
        foreach (var id in wanted)
        {
            Grant(id);
        }
    }
}
