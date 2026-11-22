using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// Tasks. Anyone may create a task for themselves; assigning others needs tasks.assign. A task's creator,
/// its assignees and holders of tasks.assign may change it.
/// </summary>
[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(TaskService tasks) : ControllerBase
{
    /// <summary>
    /// Lists tasks. Filters: <c>assigneeId</c>, <c>createdById</c>, <c>assignedByMe</c> (created by me for
    /// others), <c>projectId</c>, <c>statuses</c>, <c>open</c>, <c>overdue</c>, <c>search</c>.
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<PagedResponse<TaskResponse>>> List(
        [FromQuery] Guid? assigneeId, [FromQuery] Guid? createdById, [FromQuery] bool assignedByMe, [FromQuery] Guid? projectId,
        [FromQuery] string[]? statuses, [FromQuery] bool? open, [FromQuery] bool overdue, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ApiResponse<PagedResponse<TaskResponse>>.Ok(await tasks.ListAsync(
            new TaskQuery(assigneeId, createdById, assignedByMe, projectId, statuses, open, overdue, search), page, pageSize, ct));

    /// <summary>Open tasks assigned to the signed-in user.</summary>
    [HttpGet("my-tasks")]
    public async Task<ApiResponse<PagedResponse<TaskResponse>>> MyTasks(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ApiResponse<PagedResponse<TaskResponse>>.Ok(await tasks.MyTasksAsync(page, pageSize, ct));

    /// <summary>Open tasks past their due time; <c>mine=true</c> for the signed-in user's only.</summary>
    [HttpGet("overdue")]
    public async Task<ApiResponse<PagedResponse<TaskResponse>>> Overdue(
        [FromQuery] bool mine, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ApiResponse<PagedResponse<TaskResponse>>.Ok(await tasks.OverdueAsync(mine, page, pageSize, ct));

    [HttpPost]
    [ProducesResponseType<ApiResponse<TaskResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<TaskResponse>>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var created = await tasks.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<TaskResponse>.Ok(created));
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<TaskResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<TaskResponse>.Ok(await tasks.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<TaskResponse>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct) =>
        ApiResponse<TaskResponse>.Ok(await tasks.UpdateAsync(id, request, ct));

    /// <summary>Changes the status. Completing records who completed it and when.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ApiResponse<TaskResponse>> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        ApiResponse<TaskResponse>.Ok(await tasks.ChangeStatusAsync(id, request, ct));

    /// <summary>Adds an assignee, or makes an existing one the primary responsible person.</summary>
    [HttpPost("{id:guid}/assignments")]
    public async Task<ApiResponse<TaskResponse>> Assign(Guid id, AssignTaskRequest request, CancellationToken ct) =>
        ApiResponse<TaskResponse>.Ok(await tasks.AssignAsync(id, request, ct));

    [HttpDelete("{id:guid}/assignments/{userId:guid}")]
    public async Task<ApiResponse<TaskResponse>> Unassign(Guid id, Guid userId, CancellationToken ct) =>
        ApiResponse<TaskResponse>.Ok(await tasks.UnassignAsync(id, userId, ct));

    /// <summary>The task's change history, newest first.</summary>
    [HttpGet("{id:guid}/history")]
    public async Task<ApiResponse<IReadOnlyList<ActivityEntryResponse>>> History(Guid id, CancellationToken ct) =>
        ApiResponse<IReadOnlyList<ActivityEntryResponse>>.Ok(await tasks.HistoryAsync(id, ct));
}
