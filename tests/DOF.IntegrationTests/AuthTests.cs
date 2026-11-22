using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Hosting;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class AuthTests(DofApiFactory factory)
{
    [Fact]
    public async Task Valid_credentials_return_tokens_and_the_profile()
    {
        var tokens = await factory.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword);

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        Assert.InRange(tokens.AccessTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        Assert.Contains(SystemRoles.Administrator, tokens.User.Roles);
        Assert.Equal(Permissions.All.Keys.Order(), tokens.User.Permissions);
    }

    [Fact]
    public async Task Email_is_matched_case_insensitively()
    {
        var tokens = await factory.LoginAsync("  ADMIN@Test.Local ", DofApiFactory.AdminPassword);

        Assert.Equal(DofApiFactory.AdminEmail, tokens.User.Email);
    }

    [Theory]
    [InlineData(DofApiFactory.AdminEmail, "wrong-password-123")]
    [InlineData("nobody@test.local", "whatever-password")]
    public async Task Invalid_credentials_are_rejected_with_a_generic_message(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Empty_login_request_fails_validation()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Email", body);
        Assert.Contains("Password", body);
    }

    [Fact]
    public async Task Me_requires_a_valid_access_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().WithToken("not-a-real-token").GetAsync("/api/v1/auth/me", Ct)).StatusCode);

        var tokens = await factory.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword);
        var me = await (await factory.CreateClient().WithToken(tokens.AccessToken).GetAsync("/api/v1/auth/me", Ct))
            .DataAsync<CurrentUserResponse>();

        Assert.Equal(DofApiFactory.AdminEmail, me.Email);
        Assert.Equal("Africa/Nairobi", me.TimeZone);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_whole_session()
    {
        var client = factory.CreateClient();
        var first = await factory.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword);

        var second = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(first.RefreshToken), Ct))
            .DataAsync<AuthTokensResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Replaying the already-rotated token is treated as theft...
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(first.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...so the legitimate successor is revoked too.
        var successor = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(second.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = factory.CreateClient();
        var tokens = await factory.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Logging out again is harmless.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken), Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_password_ends_old_sessions_and_replaces_the_password()
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var original = await factory.LoginAsync(user.Email, user.Password);
        const string newPassword = "A-Brand-New-Password";

        var changed = await (await factory.CreateClient().WithToken(original.AccessToken).PostAsJsonAsync(
                "/api/v1/auth/change-password", new ChangePasswordRequest(user.Password, newPassword), Ct))
            .DataAsync<AuthTokensResponse>();

        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(original.RefreshToken), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user.Email, user.Password), Ct)).StatusCode);
        await factory.LoginAsync(user.Email, newPassword);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(changed.RefreshToken), Ct)).StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_a_wrong_current_password()
    {
        var user = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var tokens = await factory.LoginAsync(user.Email, user.Password);

        var response = await factory.CreateClient().WithToken(tokens.AccessToken).PostAsJsonAsync(
            "/api/v1/auth/change-password", new ChangePasswordRequest("not-my-password", "Another-New-Password"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoints_are_rate_limited()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimitPerMinute", "3"));
        var client = limited.CreateClient();
        var request = new LoginRequest("nobody@test.local", "whatever-password");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/auth/login", request, Ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests], statuses);
    }
}
