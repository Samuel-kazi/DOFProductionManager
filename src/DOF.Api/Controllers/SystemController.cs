using System.Reflection;
using DOF.Application.Common;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DOF.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[AllowAnonymous]
public sealed class SystemController(
    IOptions<OrganisationOptions> organisation,
    IWebHostEnvironment environment,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Basic information about the running API, used by the desktop client to verify connectivity.</summary>
    [HttpGet("info")]
    [ProducesResponseType<ApiResponse<SystemInfoResponse>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<SystemInfoResponse>> GetInfo()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        return ApiResponse<SystemInfoResponse>.Ok(new SystemInfoResponse(
            "DOF Production Manager API",
            version,
            environment.EnvironmentName,
            clock.GetUtcNow(),
            organisation.Value.DefaultTimeZone));
    }
}
