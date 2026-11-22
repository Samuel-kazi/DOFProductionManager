using DOF.Domain.Entities;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;

namespace DOF.UnitTests;

public class IdentityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Emails_are_trimmed_and_lower_cased()
    {
        var user = User.Create(" Ada ", "  Ada@DOF.Org ", "hash", "Africa/Nairobi");

        Assert.Equal("ada@dof.org", user.Email);
        Assert.Equal("Ada", user.FullName);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void A_user_must_keep_at_least_one_role()
    {
        var user = User.Create("Ada", "ada@dof.org", "hash", "Africa/Nairobi");

        Assert.Throws<DomainException>(() => user.SetRoles([]));
    }

    [Fact]
    public void Setting_roles_adds_and_removes_without_duplicates()
    {
        var user = User.Create("Ada", "ada@dof.org", "hash", "Africa/Nairobi");
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        user.SetRoles([a, b, b]);
        user.SetRoles([b, c]);

        Assert.Equal(new[] { b, c }.Order(), user.Roles.Select(r => r.RoleId).Order());
    }

    [Fact]
    public void Role_permissions_are_replaced_as_a_set()
    {
        var role = new Role("Editor", "Edits");
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        role.SetPermissions([a, b]);
        role.SetPermissions([b]);
        role.Grant(b);

        Assert.Equal([b], role.Permissions.Select(p => p.PermissionId));
    }

    [Fact]
    public void Refresh_tokens_expire_and_revoke_once()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", Guid.NewGuid(), Now.AddDays(1), "Mac");

        Assert.True(token.IsUsable(Now));
        Assert.False(token.IsUsable(Now.AddDays(2)));

        var replacement = Guid.NewGuid();
        token.Revoke(Now, RefreshToken.Reasons.Rotated, replacement);
        token.Revoke(Now.AddHours(1), RefreshToken.Reasons.LoggedOut);

        Assert.False(token.IsUsable(Now));
        Assert.Equal(RefreshToken.Reasons.Rotated, token.RevocationReason);
        Assert.Equal(replacement, token.ReplacedByTokenId);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void Default_role_grants_only_reference_known_permissions()
    {
        Assert.All(SystemRoles.All.SelectMany(r => r.DefaultPermissions),
            p => Assert.True(Permissions.All.ContainsKey(p), $"Unknown permission '{p}'"));

        Assert.Equal(Permissions.All.Keys.Order(),
            SystemRoles.All.Single(r => r.Name == SystemRoles.Administrator).DefaultPermissions.Order());
        Assert.Empty(SystemRoles.All.Single(r => r.Name == SystemRoles.TeamMember).DefaultPermissions);
        Assert.DoesNotContain(Permissions.UsersManage,
            SystemRoles.All.Single(r => r.Name == SystemRoles.ProductionManager).DefaultPermissions);
    }
}
