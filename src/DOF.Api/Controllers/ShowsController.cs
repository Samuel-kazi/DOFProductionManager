using DOF.Application.Shows;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// Shows, seasons and episodes. Every signed-in user can read; changes are checked per record
/// (episodes.manage, the show's producer, the episode's producer, or — for status — the episode's crew).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ShowsController(ShowService shows, EpisodeService episodes) : ControllerBase
{
    // ---------- shows ----------

    [HttpGet("shows")]
    public async Task<ApiResponse<PagedResponse<ShowSummaryResponse>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] string[]? statuses,
        [FromQuery] string? showType, [FromQuery] Guid? producerId, [FromQuery] bool includeArchived, CancellationToken ct) =>
        ApiResponse<PagedResponse<ShowSummaryResponse>>.Ok(
            await shows.ListAsync(page, pageSize, search, statuses, showType, producerId, includeArchived, ct));

    [HttpPost("shows")]
    [ProducesResponseType<ApiResponse<ShowResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ShowResponse>>> Create(CreateShowRequest request, CancellationToken ct)
    {
        var created = await shows.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<ShowResponse>.Ok(created));
    }

    [HttpGet("shows/{id:guid}")]
    public async Task<ApiResponse<ShowResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<ShowResponse>.Ok(await shows.GetAsync(id, ct));

    [HttpPut("shows/{id:guid}")]
    public async Task<ApiResponse<ShowResponse>> Update(Guid id, UpdateShowRequest request, CancellationToken ct) =>
        ApiResponse<ShowResponse>.Ok(await shows.UpdateAsync(id, request, ct));

    /// <summary>Changes the show's status. Archiving or restoring needs episodes.manage.</summary>
    [HttpPatch("shows/{id:guid}/status")]
    public async Task<ApiResponse<ShowResponse>> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        ApiResponse<ShowResponse>.Ok(await shows.ChangeStatusAsync(id, request, ct));

    /// <summary>The history of the show and its seasons.</summary>
    [HttpGet("shows/{id:guid}/history")]
    public async Task<ApiResponse<IReadOnlyList<ActivityEntryResponse>>> History(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ActivityEntryResponse>>.Ok(await shows.HistoryAsync(id, ct));

    // ---------- seasons ----------

    [HttpGet("shows/{id:guid}/seasons")]
    public async Task<ApiResponse<IReadOnlyList<SeasonResponse>>> Seasons(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<SeasonResponse>>.Ok(await shows.ListSeasonsAsync(id, ct));

    /// <summary>Adds a season; without a number it becomes the show's next season.</summary>
    [HttpPost("shows/{id:guid}/seasons")]
    [ProducesResponseType<ApiResponse<SeasonResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<SeasonResponse>>> CreateSeason(Guid id, CreateSeasonRequest request, CancellationToken ct)
    {
        var created = await shows.CreateSeasonAsync(id, request, ct);
        return CreatedAtAction(nameof(GetSeason), new { id = created.Id }, ApiResponse<SeasonResponse>.Ok(created));
    }

    [HttpGet("seasons/{id:guid}")]
    public async Task<ApiResponse<SeasonResponse>> GetSeason(Guid id, CancellationToken ct) =>
        ApiResponse<SeasonResponse>.Ok(await shows.GetSeasonAsync(id, ct));

    [HttpPut("seasons/{id:guid}")]
    public async Task<ApiResponse<SeasonResponse>> UpdateSeason(Guid id, UpdateSeasonRequest request, CancellationToken ct) =>
        ApiResponse<SeasonResponse>.Ok(await shows.UpdateSeasonAsync(id, request, ct));

    // ---------- episodes ----------

    /// <summary>Every episode of the show, in season and episode order (episodes without a season last).</summary>
    [HttpGet("shows/{id:guid}/episodes")]
    public async Task<ApiResponse<IReadOnlyList<EpisodeSummaryResponse>>> ShowEpisodes(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<EpisodeSummaryResponse>>.Ok(await episodes.ForShowAsync(id, ct));

    /// <summary>Creates an episode directly under the show, for one-off productions without seasons.</summary>
    [HttpPost("shows/{id:guid}/episodes")]
    [ProducesResponseType<ApiResponse<EpisodeResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<EpisodeResponse>>> CreateShowEpisode(
        Guid id, CreateEpisodeRequest request, CancellationToken ct)
    {
        var created = await episodes.CreateInShowAsync(id, request, ct);
        return CreatedAtAction(nameof(GetEpisode), new { id = created.Id }, ApiResponse<EpisodeResponse>.Ok(created));
    }

    [HttpGet("seasons/{id:guid}/episodes")]
    public async Task<ApiResponse<IReadOnlyList<EpisodeSummaryResponse>>> SeasonEpisodes(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<EpisodeSummaryResponse>>.Ok(await episodes.ForSeasonAsync(id, ct));

    /// <summary>Creates an episode in the season; without a number it is numbered next.</summary>
    [HttpPost("seasons/{id:guid}/episodes")]
    [ProducesResponseType<ApiResponse<EpisodeResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<EpisodeResponse>>> CreateSeasonEpisode(
        Guid id, CreateEpisodeRequest request, CancellationToken ct)
    {
        var created = await episodes.CreateInSeasonAsync(id, request, ct);
        return CreatedAtAction(nameof(GetEpisode), new { id = created.Id }, ApiResponse<EpisodeResponse>.Ok(created));
    }

    /// <summary>
    /// Lists episodes across shows. Filters: <c>showId</c>, <c>seasonId</c>, <c>assigneeId</c> (producer or
    /// crew), <c>mine</c>, <c>statuses</c>, <c>open</c>, <c>search</c>.
    /// </summary>
    [HttpGet("episodes")]
    public async Task<ApiResponse<PagedResponse<EpisodeSummaryResponse>>> ListEpisodes(
        [FromQuery] Guid? showId, [FromQuery] Guid? seasonId, [FromQuery] Guid? assigneeId, [FromQuery] bool mine,
        [FromQuery] string[]? statuses, [FromQuery] bool? open, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] ShowAccess access, CancellationToken ct) =>
        ApiResponse<PagedResponse<EpisodeSummaryResponse>>.Ok(await episodes.ListAsync(
            new EpisodeQuery(showId, seasonId, AssigneeId: mine ? access.Me : assigneeId, Statuses: statuses, Open: open, Search: search),
            page, pageSize, ct));

    [HttpGet("episodes/{id:guid}")]
    public async Task<ApiResponse<EpisodeResponse>> GetEpisode(Guid id, CancellationToken ct) =>
        ApiResponse<EpisodeResponse>.Ok(await episodes.GetAsync(id, ct));

    /// <summary>Updates an episode, including its recording time and deadlines; requires the current version.</summary>
    [HttpPut("episodes/{id:guid}")]
    public async Task<ApiResponse<EpisodeResponse>> UpdateEpisode(Guid id, UpdateEpisodeRequest request, CancellationToken ct) =>
        ApiResponse<EpisodeResponse>.Ok(await episodes.UpdateAsync(id, request, ct));

    /// <summary>Sets the production status (corrections allowed; history is kept). Publication statuses follow releases.</summary>
    [HttpPatch("episodes/{id:guid}/status")]
    public async Task<ApiResponse<EpisodeResponse>> ChangeEpisodeStatus(
        Guid id, ChangeEpisodeStatusRequest request, CancellationToken ct) =>
        ApiResponse<EpisodeResponse>.Ok(await episodes.ChangeStatusAsync(id, request, ct));

    /// <summary>Assigns a person to a responsibility on the episode (a person may hold several).</summary>
    [HttpPost("episodes/{id:guid}/assignments")]
    public async Task<ApiResponse<EpisodeResponse>> Assign(Guid id, AssignEpisodeRequest request, CancellationToken ct) =>
        ApiResponse<EpisodeResponse>.Ok(await episodes.AssignAsync(id, request, ct));

    [HttpDelete("episodes/{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<ApiResponse<EpisodeResponse>> Unassign(Guid id, Guid assignmentId, CancellationToken ct) =>
        ApiResponse<EpisodeResponse>.Ok(await episodes.UnassignAsync(id, assignmentId, ct));

    [HttpGet("episodes/{id:guid}/history")]
    public async Task<ApiResponse<IReadOnlyList<ActivityEntryResponse>>> EpisodeHistory(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ActivityEntryResponse>>.Ok(await episodes.HistoryAsync(id, ct));
}
