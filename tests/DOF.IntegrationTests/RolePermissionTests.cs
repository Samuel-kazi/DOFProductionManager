using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class RolePermissionTests(DofApiFactory factory)
{
    [Fact]
    public async Task System_roles_and_permissions_are_seeded()
    {
        var admin = await factory.AdminClientAsync();

        var roles = await (await admin.GetAsync("/api/v1/roles", Ct)).DataAsync<List<RoleResponse>>();
        var permissions = await (await admin.GetAsync("/api/v1/permissions", Ct)).DataAsync<List<PermissionResponse>>();

        Assert.Equal(SystemRoles.All.Select(r => r.Name).Order(), roles.Select(r => r.Name).Order());
        Assert.Equal(Permissions.All.Keys.Order(), permissions.Select(p => p.Name));
        Assert.Equal(Permissions.All.Keys.Order(), roles.Single(r => r.Name == SystemRoles.Administrator).Permissions);
    }

    [Fact]
    public async Task Permission_changes_take_effect_without_signing_in_again()
    {
        var admin = await factory.AdminClientAsync();
        var teamRole = (await (await admin.GetAsync("/api/v1/roles", Ct)).DataAsync<List<RoleResponse>>())
            .Single(r => r.Name == SystemRoles.TeamMember);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var memberClient = factory.CreateClient().WithToken((await factory.LoginAsync(member.Email, member.Password)).AccessToken);
        CreateUserRequest NewUser() => new("Created By Member", $"m-{Guid.NewGuid():N}@test.local", "Long-Enough-Pass", null, [teamRole.Id]);

        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync("/api/v1/users", NewUser(), Ct)).StatusCode);

            await (await admin.PutAsJsonAsync($"/api/v1/roles/{teamRole.Id}/permissions",
                new UpdateRolePermissionsRequest([Permissions.UsersManage]), Ct)).DataAsync<RoleResponse>();
            Assert.Equal(HttpStatusCode.Created, (await memberClient.PostAsJsonAsync("/api/v1/users", NewUser(), Ct)).StatusCode);
        }
        finally
        {
            await (await admin.PutAsJsonAsync($"/api/v1/roles/{teamRole.Id}/permissions",
                new UpdateRolePermissionsRequest(teamRole.Permissions), Ct)).DataAsync<RoleResponse>();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync("/api/v1/users", NewUser(), Ct)).StatusCode);
    }

    [Fact]
    public async Task Unknown_permissions_and_admin_lockout_are_rejected()
    {
        var admin = await factory.AdminClientAsync();
        var adminRoleId = await admin.RoleIdAsync(SystemRoles.Administrator);

        var unknown = await admin.PutAsJsonAsync($"/api/v1/roles/{adminRoleId}/permissions",
            new UpdateRolePermissionsRequest([Permissions.UsersManage, "made.up"]), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);

        var lockout = await admin.PutAsJsonAsync($"/api/v1/roles/{adminRoleId}/permissions",
            new UpdateRolePermissionsRequest([Permissions.ReportsView]), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lockout.StatusCode);
    }

    [Fact]
    public async Task Only_user_managers_can_change_role_permissions()
    {
        var producer = await factory.CreateUserAsync(SystemRoles.Producer);
        var client = factory.CreateClient().WithToken((await factory.LoginAsync(producer.Email, producer.Password)).AccessToken);
        var producerRoleId = await client.RoleIdAsync(SystemRoles.Producer);

        var response = await client.PutAsJsonAsync($"/api/v1/roles/{producerRoleId}/permissions",
            new UpdateRolePermissionsRequest([.. Permissions.All.Keys]), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
