using DOF.Application.Dashboard;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Today's schedule and the coming week's items assigned to the signed-in user.</summary>
    [HttpGet("personal")]
    public async Task<ApiResponse<PersonalDashboardResponse>> Personal([FromQuery] string? timeZone, CancellationToken ct) =>
        ApiResponse<PersonalDashboardResponse>.Ok(await dashboard.GetPersonalAsync(timeZone, ct));

    /// <summary>Organisation-wide upcoming activities and those with nobody responsible. Requires <c>reports.view</c>.</summary>
    [HttpGet("management")]
    [Authorize(Policy = Permissions.ReportsView)]
    public async Task<ApiResponse<ManagementDashboardResponse>> Management(
        [FromQuery] DateOnly? start, [FromQuery] DateOnly? end, [FromQuery] string? timeZone, CancellationToken ct) =>
        ApiResponse<ManagementDashboardResponse>.Ok(await dashboard.GetManagementAsync(start, end, timeZone, ct));

    /// <summary>Upcoming items for the next <paramref name="days"/> days (default 7); <paramref name="mine"/> limits to the user's.</summary>
    [HttpGet("upcoming")]
    public async Task<ApiResponse<CalendarRangeResponse>> Upcoming(
        [FromQuery] int? days, [FromQuery] bool mine, [FromQuery] string? timeZone, CancellationToken ct) =>
        ApiResponse<CalendarRangeResponse>.Ok(await dashboard.GetUpcomingAsync(days, mine, timeZone, ct));
}
