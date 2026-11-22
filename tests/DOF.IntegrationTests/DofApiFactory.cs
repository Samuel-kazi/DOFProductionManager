using DOF.Application.Common.Abstractions;
using DOF.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

[assembly: AssemblyFixture(typeof(DOF.IntegrationTests.DofApiFactory))]
// Tests share one database and some change shared state (role permissions), so they run sequentially.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace DOF.IntegrationTests;

/// <summary>
/// Hosts the real API against a real PostgreSQL test database (never EF Core's in-memory provider).
/// The schema is dropped and rebuilt from migrations once per test run.
/// Override the database with the DOF_TEST_DATABASE environment variable.
/// </summary>
public sealed class DofApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Test-Admin-Password-1";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DOF_TEST_DATABASE")
        ?? "Host=localhost;Port=5432;Database=dof_production_manager_test;Username=dof_pm";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-not-for-production-use");
        builder.UseSetting($"{BootstrapOptions.SectionName}:AdminEmail", AdminEmail);
        builder.UseSetting($"{BootstrapOptions.SectionName}:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimiting:AuthPermitLimitPerMinute", "10000");
        // Jobs run directly in tests (with chosen times); Hangfire itself is covered by one dedicated test.
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Email);
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    /// <summary>The e-mail sender every test host uses.</summary>
    public TestEmailSender Email { get; } = new();

    /// <summary>Runs work (typically a background job) in its own DI scope, as Hangfire would.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public async ValueTask InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var reset = new NpgsqlCommand("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;", connection);
            await reset.ExecuteNonQueryAsync();
        }

        // Starting the host applies migrations and seeds roles, permissions and the bootstrap administrator.
        _ = Services;
    }
}
