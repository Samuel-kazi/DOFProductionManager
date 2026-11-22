namespace DOF.Infrastructure.Persistence;

/// <summary>
/// Creates the first Administrator when the database has no users. Bound from <c>Bootstrap</c>;
/// supply the password through user-secrets or an environment variable, never a committed file.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "System Administrator";
}
