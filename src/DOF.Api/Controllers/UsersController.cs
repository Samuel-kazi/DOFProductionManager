using DOF.Application.Common.Abstractions;
using DOF.Application.Users;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(UserService users, WorkloadService workload, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Lists users (any signed-in user, e.g. for assignment pickers).</summary>
    [HttpGet]
    public async Task<ApiResponse<PagedResponse<UserResponse>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] bool? isActive,
        CancellationToken ct) =>
        ApiResponse<PagedResponse<UserResponse>>.Ok(await users.ListAsync(page, pageSize, search, isActive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<UserResponse>> Get(Guid id, CancellationToken ct) =>
        ApiResponse<UserResponse>.Ok(await users.GetAsync(id, ct));

    /// <summary>The person's open and overdue tasks and open milestones.</summary>
    [HttpGet("{id:guid}/workload")]
    public async Task<ApiResponse<WorkloadResponse>> Workload(Guid id, CancellationToken ct) =>
        ApiResponse<WorkloadResponse>.Ok(await workload.ForUserAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permissions.UsersManage)]
    [ProducesResponseType<ApiResponse<UserResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, ApiResponse<UserResponse>.Ok(user));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ApiResponse<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        ApiResponse<UserResponse>.Ok(await users.UpdateAsync(id, request, ct));

    /// <summary>Activates or deactivates an account. Deactivation ends the user's sessions immediately.</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ApiResponse<UserResponse>> SetStatus(Guid id, UpdateUserStatusRequest request, CancellationToken ct) =>
        ApiResponse<UserResponse>.Ok(await users.SetStatusAsync(id, request.IsActive, currentUser.RequiredUserId, ct));
}
