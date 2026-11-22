using DOF.Application.Common.Abstractions;
using DOF.Infrastructure.Authentication;
using DOF.Infrastructure.Persistence;
using DOF.Application.Reminders;
using DOF.Infrastructure.Persistence.Interceptors;
using DOF.Infrastructure.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Database";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<ReminderSourceInterceptor>();

        services.AddDbContext<DofDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
            }

            options
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(DofDbContext).Assembly.GetName().Name))
                .UseSnakeCaseNamingConvention()
                // The reminder interceptor adds outbox rows, so it runs before auditing stamps timestamps.
                .AddInterceptors(sp.GetRequiredService<ReminderSourceInterceptor>(), sp.GetRequiredService<AuditingInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<DofDbContext>());

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.IsValid(),
                $"Jwt settings are invalid: Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes, " +
                "AccessTokenMinutes 1-60 and RefreshTokenDays 1-90.")
            .ValidateOnStart();
        services.AddOptions<BootstrapOptions>().Bind(configuration.GetSection(BootstrapOptions.SectionName));

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        services.AddOptions<ReminderOptions>().Bind(configuration.GetSection(ReminderOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IJobStore, PostgresJobStore>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddBackgroundJobs(configuration, configuration.GetConnectionString(ConnectionStringName) ?? string.Empty);

        services.AddHealthChecks()
            .AddDbContextCheck<DofDbContext>("database", tags: ["ready"]);

        return services;
    }
}
