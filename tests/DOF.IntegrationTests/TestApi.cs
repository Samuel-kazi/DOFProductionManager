using System.Net.Http.Headers;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;

namespace DOF.IntegrationTests;

/// <summary>Helpers for driving the API the way a client would.</summary>
public static class TestApi
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<T> DataAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Expected success but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Ct);
        return body!.Data!;
    }

    public static async Task<AuthTokensResponse> LoginAsync(this DofApiFactory factory, string email, string password) =>
        await (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), Ct))
            .DataAsync<AuthTokensResponse>();

    public static async Task<HttpClient> AdminClientAsync(this DofApiFactory factory) =>
        factory.CreateClient().WithToken(
            (await factory.LoginAsync(DofApiFactory.AdminEmail, DofApiFactory.AdminPassword)).AccessToken);

    public static async Task<Guid> RoleIdAsync(this HttpClient client, string roleName) =>
        (await (await client.GetAsync("/api/v1/roles", Ct)).DataAsync<List<RoleResponse>>())
        .Single(r => r.Name == roleName).Id;

    public sealed record CreatedUser(Guid Id, string Email, string Password);

    /// <summary>Creates a user with a unique email via the admin API.</summary>
    public static async Task<CreatedUser> CreateUserAsync(this DofApiFactory factory, string roleName)
    {
        var admin = await factory.AdminClientAsync();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        const string password = "Correct-Horse-Battery";

        var user = await (await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
                "Test User", email, password, null, [await admin.RoleIdAsync(roleName)]), Ct))
            .DataAsync<UserResponse>();

        return new CreatedUser(user.Id, email, password);
    }
}
