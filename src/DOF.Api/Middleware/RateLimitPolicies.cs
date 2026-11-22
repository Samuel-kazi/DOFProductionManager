using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DOF.Api.Middleware;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per client IP per minute across the authentication endpoints.</summary>
    public int AuthPermitLimitPerMinute { get; set; } = 10;
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";

    public static IServiceCollection AddDofRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, context =>
            {
                var limit = context.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitingOptions>>().Value
                    .AuthPermitLimitPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
