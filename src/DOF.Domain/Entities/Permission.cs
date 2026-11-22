namespace DOF.Domain.Entities;

public sealed class Permission : Entity
{
    private Permission() { }

    public Permission(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
}
