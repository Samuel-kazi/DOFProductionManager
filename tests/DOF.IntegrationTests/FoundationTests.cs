using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.IntegrationTests;

public class FoundationTests(DofApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoint_reports_healthy_database()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.RootElement.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Liveness_endpoint_responds_without_dependencies()
    {
        var response = await factory.CreateClient().GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Database_is_reachable_with_all_migrations_applied()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DofDbContext>();

        Assert.True(await db.Database.CanConnectAsync(Ct));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync(Ct));
    }

    [Fact]
    public async Task System_info_uses_standard_response_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/system/info", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SystemInfoResponse>>(Ct);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("Africa/Nairobi", body.Data!.DefaultTimeZone);
        Assert.Equal(TimeSpan.Zero, body.Data.ServerTimeUtc.Offset);
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_document_is_not_exposed_outside_development()
    {
        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
