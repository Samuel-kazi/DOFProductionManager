using DOF.Application.Auth;
using DOF.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DOF.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UserProfileQueries>();
        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
