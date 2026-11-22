using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class UserManagementTests(DofApiFactory factory)
{
    [Fact]
    public async Task Protected_endpoints_reject_anonymous_requests()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/roles", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("X", "x@test.local", "Long-Enough-Pass", null, [Guid.NewGuid()]), Ct)).StatusCode);
    }

    [Fact]
    public async Task Team_members_can_list_users_but_not_manage_them()
    {
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var client = factory.CreateClient().WithToken((await factory.LoginAsync(member.Email, member.Password)).AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users", Ct)).StatusCode);

        var create = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
            "Someone", $"someone-{Guid.NewGuid():N}@test.local", "Long-Enough-Pass", null,
            [await client.RoleIdAsync(SystemRoles.TeamMember)]), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var deactivate = await client.PatchAsJsonAsync($"/api/v1/users/{member.Id}/status", new UpdateUserStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
    }

    [Fact]
    public async Task Administrators_can_create_and_fetch_users()
    {
        var admin = await factory.AdminClientAsync();
        var producerRole = await admin.RoleIdAsync(SystemRoles.Producer);
        var email = $"Producer-{Guid.NewGuid():N}@Test.Local";

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("  Grace Producer ", email, "Long-Enough-Pass", "Europe/London", [producerRole]), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.DataAsync<UserResponse>();
        Assert.Equal("Grace Producer", created.FullName);
        Assert.Equal(email.ToLowerInvariant(), created.Email);
        Assert.Equal("Europe/London", created.TimeZone);
        Assert.True(created.IsActive);
        Assert.Equal([SystemRoles.Producer], created.Roles.Select(r => r.Name));
        Assert.Equal(response.Headers.Location?.AbsolutePath, $"/api/v1/users/{created.Id}");

        var fetched = await (await admin.GetAsync($"/api/v1/users/{created.Id}", Ct)).DataAsync<UserResponse>();
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Email, fetched.Email);
        Assert.Equal(created.Version, fetched.Version);
        Assert.Equal(created.Roles, fetched.Roles);
    }

    [Fact]
    public async Task New_users_default_to_the_organisation_timezone()
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var admin = await factory.AdminClientAsync();

        var fetched = await (await admin.GetAsync($"/api/v1/users/{user.Id}", Ct)).DataAsync<UserResponse>();

        Assert.Equal("Africa/Nairobi", fetched.TimeZone);
    }

    [Fact]
    public async Task Invalid_user_requests_return_field_errors()
    {
        var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("", "not-an-email", "short", "Mars/Olympus", []), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        foreach (var field in new[] { "FullName", "Email", "Password", "TimeZone", "RoleIds" })
        {
            Assert.Contains($"\"{field}\"", body);
        }
    }

    [Fact]
    public async Task Duplicate_emails_are_rejected()
    {
        var existing = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
            "Copy", existing.Email.ToUpperInvariant(), "Long-Enough-Pass", null,
            [await admin.RoleIdAsync(SystemRoles.TeamMember)]), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Updates_with_a_stale_version_are_rejected()
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var admin = await factory.AdminClientAsync();
        var loaded = await (await admin.GetAsync($"/api/v1/users/{user.Id}", Ct)).DataAsync<UserResponse>();
        var roleIds = loaded.Roles.Select(r => r.Id).ToList();

        var first = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new UpdateUserRequest("First Editor", user.Email, "Africa/Nairobi", roleIds, loaded.Version), Ct);
        var firstResult = await first.DataAsync<UserResponse>();
        Assert.Equal(loaded.Version + 1, firstResult.Version);

        var second = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new UpdateUserRequest("Second Editor", user.Email, "Africa/Nairobi", roleIds, loaded.Version), Ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("modified by someone else", await second.Content.ReadAsStringAsync(Ct));
        var current = await (await admin.GetAsync($"/api/v1/users/{user.Id}", Ct)).DataAsync<UserResponse>();
        Assert.Equal("First Editor", current.FullName);
    }

    [Fact]
    public async Task Deactivation_blocks_login_and_existing_tokens_immediately()
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var tokens = await factory.LoginAsync(user.Email, user.Password);
        var userClient = factory.CreateClient().WithToken(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/auth/me", Ct)).StatusCode);

        var admin = await factory.AdminClientAsync();
        var deactivated = await (await admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}/status",
            new UpdateUserStatusRequest(false), Ct)).DataAsync<UserResponse>();
        Assert.False(deactivated.IsActive);

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(user.Email, user.Password), Ct)).StatusCode);

        await (await admin.PatchAsJsonAsync($"/api/v1/users/{user.Id}/status", new UpdateUserStatusRequest(true), Ct))
            .DataAsync<UserResponse>();
        await factory.LoginAsync(user.Email, user.Password);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_removed()
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();

        var self = await admin.PatchAsJsonAsync($"/api/v1/users/{me.Id}/status", new UpdateUserStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);

        var current = await (await admin.GetAsync($"/api/v1/users/{me.Id}", Ct)).DataAsync<UserResponse>();
        var demote = await admin.PutAsJsonAsync($"/api/v1/users/{me.Id}", new UpdateUserRequest(
            current.FullName, current.Email, current.TimeZone,
            [await admin.RoleIdAsync(SystemRoles.TeamMember)], current.Version), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, demote.StatusCode);
        Assert.Contains("At least one active Administrator", await demote.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task User_list_supports_search_and_paging()
    {
        var admin = await factory.AdminClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var teamRole = await admin.RoleIdAsync(SystemRoles.TeamMember);
        for (var i = 0; i < 3; i++)
        {
            await (await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
                $"Searchable {marker} {i}", $"s{i}-{marker}@test.local", "Long-Enough-Pass", null, [teamRole]), Ct))
                .DataAsync<UserResponse>();
        }

        var page = await (await admin.GetAsync($"/api/v1/users?search={marker.ToUpperInvariant()}&pageSize=2&page=2", Ct))
            .DataAsync<PagedResponse<UserResponse>>();

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal($"Searchable {marker} 2", Assert.Single(page.Items).FullName);
    }
}
