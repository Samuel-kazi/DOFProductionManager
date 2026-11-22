using DOF.Desktop.Services.Api;
using DOF.Desktop.Services.Security;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests.Desktop;

/// <summary>The desktop client's session service, exercised against the real API.</summary>
public class AuthSessionTests(DofApiFactory factory)
{
    private sealed class InMemorySecureStore : ISecureStore
    {
        public Dictionary<string, string> Items { get; } = [];
        public string? Get(string key) => Items.GetValueOrDefault(key);
        public void Set(string key, string value) => Items[key] = value;
        public void Remove(string key) => Items.Remove(key);
    }

    [Fact]
    public async Task Login_stores_only_the_refresh_token_and_restores_after_restart()
    {
        var store = new InMemorySecureStore();
        using (var session = new AuthSession(factory.CreateClient(), store))
        {
            var result = await session.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword, Ct);

            Assert.True(result.Succeeded);
            Assert.Equal(DofApiFactory.AdminEmail, session.CurrentUser!.Email);
            Assert.NotNull(await session.GetAccessTokenAsync(Ct));
            Assert.Equal([AuthSession.RefreshTokenKey], store.Items.Keys);
        }

        // A new session (app restart) signs in silently from the secure store.
        using var restarted = new AuthSession(factory.CreateClient(), store);
        Assert.True(await restarted.TryRestoreAsync(Ct));
        Assert.Contains(SystemRoles.Administrator, restarted.CurrentUser!.Roles);
    }

    [Fact]
    public async Task Failed_login_reports_an_error_and_stores_nothing()
    {
        var store = new InMemorySecureStore();
        using var session = new AuthSession(factory.CreateClient(), store);

        var result = await session.LoginAsync(DofApiFactory.AdminEmail, "wrong-password-123", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid email or password.", result.Error);
        Assert.False(session.IsSignedIn);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Logout_revokes_the_server_session_and_clears_the_store()
    {
        var store = new InMemorySecureStore();
        using var session = new AuthSession(factory.CreateClient(), store);
        await session.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword, Ct);
        var refreshToken = store.Items[AuthSession.RefreshTokenKey];

        await session.LogoutAsync(Ct);

        Assert.False(session.IsSignedIn);
        Assert.Empty(store.Items);

        // Even if the token had been copied, it no longer works.
        store.Set(AuthSession.RefreshTokenKey, refreshToken);
        using var other = new AuthSession(factory.CreateClient(), store);
        Assert.False(await other.TryRestoreAsync(Ct));
        Assert.Empty(store.Items);
    }
}
