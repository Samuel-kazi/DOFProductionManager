using DOF.Application.Common.Abstractions;
using DOF.Infrastructure.Authentication;
using DOF.Infrastructure.Persistence;
using DOF.Infrastructure.Persistence.Interceptors;
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
                .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
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

        services.AddHealthChecks()
            .AddDbContextCheck<DofDbContext>("database", tags: ["ready"]);

        return services;
    }
}
