using DOF.Application.Reports;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// Reports (spec 5.10), all requiring reports.view. Each returns a table as JSON, or as CSV with
/// <c>format=csv</c>. Times are in the caller's timezone unless <c>timeZone</c> is given.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Permissions.ReportsView)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    /// <summary>The Monday-to-Sunday week containing <c>date</c> (default this week).</summary>
    [HttpGet("weekly")]
    public async Task<IActionResult> Weekly([FromQuery] DateOnly? date, [FromQuery] string? timeZone, [FromQuery] string? format,
        CancellationToken ct) => Result(await reports.WeeklyAsync(date, timeZone, ct), format);

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? timeZone,
        [FromQuery] string? format, CancellationToken ct) => Result(await reports.MonthlyAsync(year, month, timeZone, ct), format);

    /// <summary>Releases in the next <c>days</c> (1–180, default 30).</summary>
    [HttpGet("upcoming-releases")]
    public async Task<IActionResult> UpcomingReleases([FromQuery] int? days, [FromQuery] string? timeZone, [FromQuery] string? format,
        CancellationToken ct) => Result(await reports.UpcomingReleasesAsync(days, timeZone, ct), format);

    /// <summary>Recorded but not yet published everywhere.</summary>
    [HttpGet("unpublished-episodes")]
    public async Task<IActionResult> UnpublishedEpisodes([FromQuery] string? timeZone, [FromQuery] string? format, CancellationToken ct) =>
        Result(await reports.UnpublishedEpisodesAsync(timeZone, ct), format);

    [HttpGet("overdue-tasks")]
    public async Task<IActionResult> OverdueTasks([FromQuery] string? timeZone, [FromQuery] string? format, CancellationToken ct) =>
        Result(await reports.OverdueTasksAsync(timeZone, ct), format);

    [HttpGet("project-status")]
    public async Task<IActionResult> ProjectStatus([FromQuery] string? timeZone, [FromQuery] string? format, CancellationToken ct) =>
        Result(await reports.ProjectStatusAsync(timeZone, ct), format);

    [HttpGet("team-workload")]
    public async Task<IActionResult> TeamWorkload([FromQuery] string? timeZone, [FromQuery] string? format, CancellationToken ct) =>
        Result(await reports.TeamWorkloadAsync(timeZone, ct), format);

    /// <summary>Published releases between <c>from</c> and <c>to</c> (dates; default the last 90 days, at most 400).</summary>
    [HttpGet("publication-history")]
    public async Task<IActionResult> PublicationHistory([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? timeZone,
        [FromQuery] string? format, CancellationToken ct) => Result(await reports.PublicationHistoryAsync(from, to, timeZone, ct), format);

    private IActionResult Result(ReportResponse report, string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(CsvWriter.Write(report), "text/csv; charset=utf-8", CsvWriter.FileName(report))
            : Ok(ApiResponse<ReportResponse>.Ok(report));
}

/// <summary>Global search across projects, shows, episodes, tasks, events and releases.</summary>
[ApiController]
[Route("api/v1/search")]
public sealed class SearchController(SearchService search) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<SearchResultResponse>>> Search([FromQuery] string? q, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<SearchResultResponse>>.Ok(await search.SearchAsync(q, ct));
}
