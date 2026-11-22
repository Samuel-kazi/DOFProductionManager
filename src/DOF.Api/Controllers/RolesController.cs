using DOF.Application.Common.Abstractions;
using DOF.Application.Users;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class RolesController(RoleService roles, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<ApiResponse<IReadOnlyList<RoleResponse>>> List(CancellationToken ct) =>
        ApiResponse<IReadOnlyList<RoleResponse>>.Ok(await roles.ListAsync(ct));

    /// <summary>Replaces the full set of permissions granted to a role.</summary>
    [HttpPut("roles/{id:guid}/permissions")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ApiResponse<RoleResponse>> UpdatePermissions(
        Guid id, UpdateRolePermissionsRequest request, CancellationToken ct) =>
        ApiResponse<RoleResponse>.Ok(await roles.UpdatePermissionsAsync(id, request, currentUser.RequiredUserId, ct));

    [HttpGet("permissions")]
    public async Task<ApiResponse<IReadOnlyList<PermissionResponse>>> ListPermissions(CancellationToken ct) =>
        ApiResponse<IReadOnlyList<PermissionResponse>>.Ok(await roles.ListPermissionsAsync(ct));
}
