using DOF.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(DOF.IntegrationTests.DofApiFactory))]

namespace DOF.IntegrationTests;

/// <summary>
/// Hosts the real API against a real PostgreSQL test database (never EF Core's in-memory provider).
/// Override the database with the DOF_TEST_DATABASE environment variable.
/// </summary>
public sealed class DofApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DOF_TEST_DATABASE")
        ?? "Host=localhost;Port=5432;Database=dof_production_manager_test;Username=dof_pm";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
    }

    public async ValueTask InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DofDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }
}
