using DOF.Application.Releases;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// Releases and distribution channels. Every signed-in user can read. Scheduling, editing, postponing and
/// cancelling need releases.schedule; confirming publication also allows the release's assignee.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ReleasesController(ReleaseService releases, ChannelService channels) : ControllerBase
{
    /// <summary>
    /// Lists releases. <c>view</c>: All (default, excludes cancelled), Ready, Scheduled, Published, Postponed,
    /// Overdue, Cancelled. Filters: <c>episodeId</c>, <c>showId</c>, <c>channelId</c>, <c>assignedTo</c>,
    /// <c>mine</c>, <c>from</c>/<c>to</c> (scheduled time), <c>search</c>.
    /// </summary>
    [HttpGet("releases")]
    public async Task<ApiResponse<PagedResponse<ReleaseResponse>>> List(
        [FromQuery] string? view, [FromQuery] Guid? episodeId, [FromQuery] Guid? showId, [FromQuery] Guid? channelId,
        [FromQuery] Guid? assignedTo, [FromQuery] bool mine, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] ReleaseAccess access,
        CancellationToken ct) =>
        ApiResponse<PagedResponse<ReleaseResponse>>.Ok(await releases.ListAsync(
            new ReleaseQuery(view, episodeId, showId, channelId, mine ? access.Me : assignedTo, from, to, search), page, pageSize, ct));

    [HttpPost("releases")]
    [ProducesResponseType<ApiResponse<ReleaseResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ReleaseResponse>>> Create(CreateReleaseRequest request, CancellationToken ct)
    {
        var created = await releases.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<ReleaseResponse>.Ok(created));
    }

    /// <summary>Open releases past their scheduled time without a publication confirmation; <c>mine=true</c> for mine.</summary>
    [HttpGet("releases/overdue")]
    public async Task<ApiResponse<IReadOnlyList<ReleaseResponse>>> Overdue(
        [FromQuery] bool mine, [FromServices] ReleaseAccess access, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ReleaseResponse>>.Ok(await releases.OverdueAsync(mine ? access.Me : null, 200, ct));

    /// <summary>Finished productions not yet distributed: their open releases, and such episodes without any release.</summary>
    [HttpGet("releases/unpublished")]
    public async Task<ApiResponse<UnpublishedContentResponse>> Unpublished(CancellationToken ct) =>
        ApiResponse<UnpublishedContentResponse>.Ok(await releases.UnpublishedAsync(ct));

    [HttpGet("releases/{id:guid}")]
    public async Task<ApiResponse<ReleaseResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.GetAsync(id, ct));

    /// <summary>Updates details and workflow status (Draft, AwaitingApproval, Ready, Scheduled, Failed); requires the version.</summary>
    [HttpPut("releases/{id:guid}")]
    public async Task<ApiResponse<ReleaseResponse>> Update(Guid id, UpdateReleaseRequest request, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.UpdateAsync(id, request, ct));

    [HttpPatch("releases/{id:guid}/schedule")]
    public async Task<ApiResponse<ReleaseResponse>> Reschedule(Guid id, RescheduleReleaseRequest request, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.RescheduleAsync(id, request, ct));

    /// <summary>Confirms publication with the actual time. Requires <c>confirmed: true</c>; never inferred from the schedule.</summary>
    [HttpPatch("releases/{id:guid}/publish")]
    public async Task<ApiResponse<ReleaseResponse>> Publish(Guid id, PublishReleaseRequest request, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.PublishAsync(id, request, ct));

    [HttpPatch("releases/{id:guid}/postpone")]
    public async Task<ApiResponse<ReleaseResponse>> Postpone(Guid id, PostponeReleaseRequest request, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.PostponeAsync(id, request, ct));

    [HttpPatch("releases/{id:guid}/cancel")]
    public async Task<ApiResponse<ReleaseResponse>> Cancel(Guid id, CancelReleaseRequest request, CancellationToken ct) =>
        ApiResponse<ReleaseResponse>.Ok(await releases.CancelAsync(id, request, ct));

    [HttpGet("releases/{id:guid}/history")]
    public async Task<ApiResponse<IReadOnlyList<ActivityEntryResponse>>> History(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ActivityEntryResponse>>.Ok(await releases.HistoryAsync(id, ct));

    /// <summary>The episode's distribution schedule: each channel with its own date and status.</summary>
    [HttpGet("episodes/{id:guid}/releases")]
    public async Task<ApiResponse<IReadOnlyList<ReleaseResponse>>> ForEpisode(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ReleaseResponse>>.Ok(await releases.ForEpisodeAsync(id, ct));

    // ---------- channels ----------

    [HttpGet("distribution-channels")]
    public async Task<ApiResponse<IReadOnlyList<DistributionChannelResponse>>> Channels(
        [FromQuery] bool includeInactive, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<DistributionChannelResponse>>.Ok(await channels.ListAsync(includeInactive, ct));

    [HttpPost("distribution-channels")]
    [ProducesResponseType<ApiResponse<DistributionChannelResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<DistributionChannelResponse>>> CreateChannel(
        DistributionChannelRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<DistributionChannelResponse>.Ok(await channels.CreateAsync(request, ct)));

    /// <summary>Renames, retypes or (de)activates a channel. Channels are never deleted.</summary>
    [HttpPut("distribution-channels/{id:guid}")]
    public async Task<ApiResponse<DistributionChannelResponse>> UpdateChannel(
        Guid id, DistributionChannelRequest request, CancellationToken ct) =>
        ApiResponse<DistributionChannelResponse>.Ok(await channels.UpdateAsync(id, request, ct));
}
