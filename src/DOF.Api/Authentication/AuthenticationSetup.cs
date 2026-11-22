using System.Security.Claims;
using DOF.Application.Auth;
using DOF.Domain.Rules;
using DOF.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DOF.Api.Authentication;

public static class AuthenticationSetup
{
    public const string PermissionClaimType = "permission";

    public static IServiceCollection AddDofAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<Application.Common.Abstractions.ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var options = jwt.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = TokenService.CreateSigningKey(options),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = AttachPermissionsAsync };
            });

        services.AddAuthorization(options =>
        {
            // Every endpoint requires a signed-in user unless it explicitly opts out with [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            // One policy per permission, named after the permission: [Authorize(Policy = Permissions.UsersManage)].
            foreach (var permission in Permissions.All.Keys)
            {
                options.AddPolicy(permission, p => p.RequireClaim(PermissionClaimType, permission));
            }
        });

        return services;
    }

    /// <summary>
    /// Loads the user's current status and permissions from the database on every request, so
    /// deactivation and permission changes apply immediately rather than when the access token expires.
    /// </summary>
    private static async Task AttachPermissionsAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity
            || !Guid.TryParse(identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            context.Fail("Token has no valid subject.");
            return;
        }

        var profiles = context.HttpContext.RequestServices.GetRequiredService<UserProfileQueries>();
        var permissions = await profiles.GetActiveUserPermissionsAsync(userId, context.HttpContext.RequestAborted);
        if (permissions is null)
        {
            context.Fail("User is inactive or no longer exists.");
            return;
        }

        identity.AddClaims(permissions.Select(p => new Claim(PermissionClaimType, p)));
    }
}
