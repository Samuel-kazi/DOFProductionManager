using DOF.Application.Calendar;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>Common calendar filters (all optional).</summary>
public sealed class CalendarFilters
{
    [FromQuery] public string[]? EventTypes { get; set; }
    [FromQuery] public string[]? SourceTypes { get; set; }
    [FromQuery] public string[]? Statuses { get; set; }
    [FromQuery] public string[]? Priorities { get; set; }
    [FromQuery] public Guid? AssignedUserId { get; set; }
    [FromQuery] public Guid? ProjectId { get; set; }
    [FromQuery] public Guid? ShowId { get; set; }
    [FromQuery] public bool IncludeCancelled { get; set; }
    /// <summary>IANA timezone for day/week/month/agenda boundaries. Defaults to the user's timezone.</summary>
    [FromQuery] public string? TimeZone { get; set; }

    public CalendarQuery ToQuery(DateTimeOffset start = default, DateTimeOffset end = default) => new(
        start, end, EventTypes, SourceTypes, Statuses, Priorities, AssignedUserId, ProjectId, ShowId, IncludeCancelled);
}

[ApiController]
[Route("api/v1/calendar")]
public sealed class CalendarController(CalendarService calendar, CalendarEventService events) : ControllerBase
{
    /// <summary>
    /// The aggregated calendar: every item overlapping [start, end), built from its original record
    /// (standalone events now; milestones, tasks, episodes and releases as those phases land).
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<CalendarItemResponse>>> Get(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end, [FromQuery] CalendarFilters filters,
        CancellationToken ct) =>
        ApiResponse<IReadOnlyList<CalendarItemResponse>>.Ok(await calendar.GetItemsAsync(filters.ToQuery(start, end), ct));

    /// <summary>One day in the user's timezone (default today).</summary>
    [HttpGet("day")]
    public async Task<ApiResponse<CalendarRangeResponse>> Day(
        [FromQuery] DateOnly? date, [FromQuery] CalendarFilters filters, CancellationToken ct) =>
        ApiResponse<CalendarRangeResponse>.Ok(await calendar.GetDayAsync(date, filters.TimeZone, filters.ToQuery(), ct));

    /// <summary>The Monday-to-Sunday week containing <paramref name="date"/> (default this week).</summary>
    [HttpGet("week")]
    public async Task<ApiResponse<CalendarRangeResponse>> Week(
        [FromQuery] DateOnly? date, [FromQuery] CalendarFilters filters, CancellationToken ct) =>
        ApiResponse<CalendarRangeResponse>.Ok(await calendar.GetWeekAsync(date, filters.TimeZone, filters.ToQuery(), ct));

    /// <summary>A calendar month (default the current month).</summary>
    [HttpGet("month")]
    public async Task<ApiResponse<CalendarRangeResponse>> Month(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] CalendarFilters filters, CancellationToken ct) =>
        ApiResponse<CalendarRangeResponse>.Ok(await calendar.GetMonthAsync(year, month, filters.TimeZone, filters.ToQuery(), ct));

    /// <summary>An agenda of <paramref name="days"/> days (default 14, max 90) from <paramref name="start"/> (default today).</summary>
    [HttpGet("agenda")]
    public async Task<ApiResponse<CalendarRangeResponse>> Agenda(
        [FromQuery] DateOnly? start, [FromQuery] int? days, [FromQuery] CalendarFilters filters, CancellationToken ct) =>
        ApiResponse<CalendarRangeResponse>.Ok(await calendar.GetAgendaAsync(start, days, filters.TimeZone, filters.ToQuery(), ct));

    /// <summary>Standalone events (paged).</summary>
    [HttpGet("events")]
    public async Task<ApiResponse<PagedResponse<CalendarEventResponse>>> ListEvents(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] bool includeCancelled,
        CancellationToken ct) =>
        ApiResponse<PagedResponse<CalendarEventResponse>>.Ok(
            await events.ListAsync(page, pageSize, search, from, to, includeCancelled, ct));

    [HttpGet("events/{id:guid}")]
    public async Task<ApiResponse<CalendarEventResponse>> GetEvent(Guid id, CancellationToken ct) =>
        ApiResponse<CalendarEventResponse>.Ok(await events.GetAsync(id, ct));

    [HttpPost("events")]
    [Authorize(Policy = Permissions.CalendarManage)]
    [ProducesResponseType<ApiResponse<CalendarEventResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<CalendarEventResponse>>> CreateEvent(
        CreateCalendarEventRequest request, CancellationToken ct)
    {
        var created = await events.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetEvent), new { id = created.Id }, ApiResponse<CalendarEventResponse>.Ok(created));
    }

    /// <summary>Updates an event; requires the version last loaded. Rescheduling changes this record — never a copy.</summary>
    [HttpPut("events/{id:guid}")]
    [Authorize(Policy = Permissions.CalendarManage)]
    public async Task<ApiResponse<CalendarEventResponse>> UpdateEvent(
        Guid id, UpdateCalendarEventRequest request, CancellationToken ct) =>
        ApiResponse<CalendarEventResponse>.Ok(await events.UpdateAsync(id, request, ct));

    /// <summary>Cancels (archives) an event. It is kept for history and removed from the calendar.</summary>
    [HttpDelete("events/{id:guid}")]
    [Authorize(Policy = Permissions.CalendarManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelEvent(Guid id, CancellationToken ct)
    {
        await events.CancelAsync(id, ct);
        return NoContent();
    }
}
