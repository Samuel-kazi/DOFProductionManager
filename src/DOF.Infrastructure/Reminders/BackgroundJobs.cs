using DOF.Application.GoogleCalendar;
using DOF.Application.Reminders;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.Infrastructure.Reminders;

/// <summary>Background job settings. Tests switch Hangfire off and run the jobs directly.</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;
    /// <summary>Whether this process executes jobs (several API instances may share one schedule).</summary>
    public bool RunServer { get; set; } = true;
}

/// <summary>
/// Hangfire entry points. Each wraps an application job with the current time; overlapping runs are
/// prevented here and by the row locks in <see cref="PostgresJobStore"/>. Hangfire's own retries are off
/// because the jobs record and retry failures themselves.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class ReminderJobRunner(
    ReminderDispatcher dispatcher, OutboxProcessor outbox, OverdueDetector overdue, EscalationJob escalation,
    MaintenanceJob maintenance, GoogleInviteSync googleInvites, TimeProvider clock)
{
    [DisableConcurrentExecution(60)]
    public async Task DispatchAsync(CancellationToken ct)
    {
        await outbox.RunAsync(clock.GetUtcNow(), ct);
        await dispatcher.RunAsync(clock.GetUtcNow(), ct);
        await outbox.RunAsync(clock.GetUtcNow(), ct);
    }

    [DisableConcurrentExecution(300)]
    public Task DetectOverdueAsync(CancellationToken ct) => overdue.RunAsync(clock.GetUtcNow(), ct);

    [DisableConcurrentExecution(300)]
    public Task EscalateAsync(CancellationToken ct) => escalation.RunAsync(clock.GetUtcNow(), ct);

    [DisableConcurrentExecution(3600)]
    public Task MaintainAsync(CancellationToken ct) => maintenance.RunAsync(clock.GetUtcNow(), ct);

    /// <summary>Pushes new and changed events to guests' Google Calendars and reads their answers.</summary>
    [DisableConcurrentExecution(120)]
    public Task SyncGoogleInvitesAsync(CancellationToken ct) => googleInvites.RunAsync(clock.GetUtcNow(), ct);
}

public static class BackgroundJobSetup
{
    public static readonly IReadOnlyDictionary<string, string> Schedules = new Dictionary<string, string>
    {
        ["reminders-dispatch"] = "* * * * *",
        ["overdue-detection"] = "*/15 * * * *",
        ["overdue-escalation"] = "0 * * * *",
        ["maintenance"] = "30 3 * * *",
        ["google-invites-sync"] = "* * * * *",
    };

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();
        services.AddScoped<ReminderJobRunner>();
        if (!options.Enabled)
        {
            return services;
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        if (options.RunServer)
        {
            services.AddHangfireServer(server => server.ServerName = $"dof-api-{Environment.MachineName}");
        }
        return services;
    }

    /// <summary>Registers (or updates) the recurring jobs. Times are UTC; the jobs work in absolute instants.</summary>
    public static void ScheduleRecurringJobs(IServiceProvider services, IConfiguration configuration)
    {
        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();
        if (!options.Enabled)
        {
            return;
        }
        var manager = services.GetRequiredService<IRecurringJobManager>();
        manager.AddOrUpdate<ReminderJobRunner>("reminders-dispatch", j => j.DispatchAsync(CancellationToken.None), Schedules["reminders-dispatch"]);
        manager.AddOrUpdate<ReminderJobRunner>("overdue-detection", j => j.DetectOverdueAsync(CancellationToken.None), Schedules["overdue-detection"]);
        manager.AddOrUpdate<ReminderJobRunner>("overdue-escalation", j => j.EscalateAsync(CancellationToken.None), Schedules["overdue-escalation"]);
        manager.AddOrUpdate<ReminderJobRunner>("maintenance", j => j.MaintainAsync(CancellationToken.None), Schedules["maintenance"]);
        manager.AddOrUpdate<ReminderJobRunner>("google-invites-sync", j => j.SyncGoogleInvitesAsync(CancellationToken.None),
            Schedules["google-invites-sync"]);
    }
}
