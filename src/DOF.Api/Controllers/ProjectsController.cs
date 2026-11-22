using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// Projects, members, milestones and timelines. Every signed-in user can read; changes are checked per
/// record (projects.create / projects.edit / the project's manager / projects.archive).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ProjectsController(ProjectService projects) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ApiResponse<PagedResponse<ProjectSummaryResponse>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] string[]? statuses,
        [FromQuery] string? projectType, [FromQuery] Guid? managerId, [FromQuery] Guid? memberId,
        [FromQuery] bool includeArchived, CancellationToken ct) =>
        ApiResponse<PagedResponse<ProjectSummaryResponse>>.Ok(await projects.ListAsync(
            page, pageSize, search, statuses, projectType, managerId, memberId, includeArchived, ct));

    [HttpPost("projects")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<ProjectResponse>.Ok(created));
    }

    [HttpGet("projects/{id:guid}")]
    public async Task<ApiResponse<ProjectResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<ProjectResponse>.Ok(await projects.GetAsync(id, ct));

    [HttpPut("projects/{id:guid}")]
    public async Task<ApiResponse<ProjectResponse>> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        ApiResponse<ProjectResponse>.Ok(await projects.UpdateAsync(id, request, ct));

    /// <summary>Changes the status. Archiving or restoring needs projects.archive.</summary>
    [HttpPatch("projects/{id:guid}/status")]
    public async Task<ApiResponse<ProjectResponse>> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        ApiResponse<ProjectResponse>.Ok(await projects.ChangeStatusAsync(id, request, ct));

    /// <summary>Milestones and tasks in date order, plus the full history of the project and its work.</summary>
    [HttpGet("projects/{id:guid}/timeline")]
    public async Task<ApiResponse<ProjectTimelineResponse>> Timeline(Guid id, CancellationToken ct) =>
        ApiResponse<ProjectTimelineResponse>.Ok(await projects.TimelineAsync(id, ct));

    /// <summary>Adds a member, or changes an existing member's project role.</summary>
    [HttpPost("projects/{id:guid}/members")]
    public async Task<ApiResponse<ProjectResponse>> SetMember(Guid id, AddProjectMemberRequest request, CancellationToken ct) =>
        ApiResponse<ProjectResponse>.Ok(await projects.SetMemberAsync(id, request, ct));

    [HttpDelete("projects/{id:guid}/members/{userId:guid}")]
    public async Task<ApiResponse<ProjectResponse>> RemoveMember(Guid id, Guid userId, CancellationToken ct) =>
        ApiResponse<ProjectResponse>.Ok(await projects.RemoveMemberAsync(id, userId, ct));

    [HttpGet("projects/{id:guid}/milestones")]
    public async Task<ApiResponse<IReadOnlyList<MilestoneResponse>>> Milestones(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<MilestoneResponse>>.Ok(await projects.ListMilestonesAsync(id, ct));

    [HttpPost("projects/{id:guid}/milestones")]
    [ProducesResponseType<ApiResponse<MilestoneResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<MilestoneResponse>>> CreateMilestone(
        Guid id, CreateMilestoneRequest request, CancellationToken ct)
    {
        var created = await projects.CreateMilestoneAsync(id, request, ct);
        return CreatedAtAction(nameof(GetMilestone), new { id = created.Id }, ApiResponse<MilestoneResponse>.Ok(created));
    }

    [HttpGet("milestones/{id:guid}")]
    public async Task<ApiResponse<MilestoneResponse>> GetMilestone(Guid id, CancellationToken ct) =>
        ApiResponse<MilestoneResponse>.Ok(await projects.GetMilestoneAsync(id, ct));

    /// <summary>Updates a milestone (including its due date and status); requires the current version.</summary>
    [HttpPut("milestones/{id:guid}")]
    public async Task<ApiResponse<MilestoneResponse>> UpdateMilestone(Guid id, UpdateMilestoneRequest request, CancellationToken ct) =>
        ApiResponse<MilestoneResponse>.Ok(await projects.UpdateMilestoneAsync(id, request, ct));

    [HttpPatch("milestones/{id:guid}/complete")]
    public async Task<ApiResponse<MilestoneResponse>> CompleteMilestone(Guid id, CancellationToken ct) =>
        ApiResponse<MilestoneResponse>.Ok(await projects.CompleteMilestoneAsync(id, ct));
}
