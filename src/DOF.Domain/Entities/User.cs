using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

public sealed class User : Entity, IVersioned
{
    private readonly List<UserRole> _roles = [];

    private User() { }

    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public string TimeZone { get; private set; } = null!;
    public int Version { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    public static User Create(string fullName, string email, string passwordHash, string timeZone) => new()
    {
        FullName = fullName.Trim(),
        Email = NormalizeEmail(email),
        PasswordHash = passwordHash,
        IsActive = true,
        TimeZone = timeZone,
    };

    /// <summary>Emails are compared case-insensitively, so they are stored trimmed and lower-cased.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void UpdateProfile(string fullName, string email, string timeZone)
    {
        FullName = fullName.Trim();
        Email = NormalizeEmail(email);
        TimeZone = timeZone;
    }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        var wanted = roleIds.ToHashSet();
        if (wanted.Count == 0)
        {
            throw new DomainException("A user must have at least one role.");
        }

        _roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        foreach (var roleId in wanted.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }
}
