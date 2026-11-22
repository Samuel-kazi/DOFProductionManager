using DOF.Application.Activity;
using DOF.Application.Auth;
using DOF.Application.Calendar;
using DOF.Application.Dashboard;
using DOF.Application.Users;
using DOF.Application.Work;
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

        // Calendar: every record type that owns dates registers an ICalendarSource.
        services.AddScoped<ICalendarSource, StandaloneEventCalendarSource>();
        services.AddScoped<ICalendarSource, MilestoneCalendarSource>();
        services.AddScoped<ICalendarSource, TaskDeadlineCalendarSource>();
        services.AddScoped<CalendarService>();
        services.AddScoped<CalendarEventService>();
        services.AddScoped<DashboardService>();

        services.AddScoped<ActivityRecorder>();
        services.AddScoped<ActivityQueries>();
        services.AddScoped<WorkAccess>();
        services.AddScoped<WorkReaders>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<WorkloadService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
