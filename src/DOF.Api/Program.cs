using DOF.Api.Authentication;
using DOF.Api.Health;
using DOF.Api.Middleware;
using DOF.Application;
using DOF.Application.Common;
using DOF.Infrastructure;
using DOF.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddOptions<OrganisationOptions>()
        .Bind(builder.Configuration.GetSection(OrganisationOptions.SectionName))
        .Validate(o => o.HasValidTimeZone(), "Organisation:DefaultTimeZone must be a valid IANA timezone.")
        .ValidateOnStart();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddDofAuthentication();
    builder.Services.AddDofRateLimiting(builder.Configuration);

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<DofExceptionHandler>();
    builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "DOF Production Manager API",
            Version = "v1",
        });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the access token returned by POST /api/v1/auth/login.",
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
        });
        var xmlDocs = Path.Combine(AppContext.BaseDirectory, "DOF.Api.xml");
        if (File.Exists(xmlDocs))
        {
            options.IncludeXmlComments(xmlDocs);
        }
    });

    var app = builder.Build();

    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        await DatabaseInitializer.ApplyMigrationsAsync(app.Services);
    }

    await DatabaseSeeder.SeedAsync(app.Services);
    DOF.Infrastructure.Reminders.BackgroundJobSetup.ScheduleRecurringJobs(app.Services, app.Configuration);

    // Request logging wraps the exception handler so it records the final status code sent to the client.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "DOF Production Manager API v1"));
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Liveness: the process is up. Readiness (/health): the process can reach its dependencies.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync,
    }).AllowAnonymous();
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = HealthResponseWriter.WriteAsync,
    }).AllowAnonymous();

    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "DOF Production Manager API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
