using DOF.Application.Common.Exceptions;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Users;

public sealed class RoleService(IApplicationDbContext db, ILogger<RoleService> logger)
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleResponse(
                r.Id,
                r.Name,
                r.Description,
                r.Permissions.Select(p => p.Permission.Name).OrderBy(n => n).ToList()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PermissionResponse(p.Name, p.Description))
            .ToListAsync(ct);

    public async Task<RoleResponse> UpdatePermissionsAsync(
        Guid roleId, UpdateRolePermissionsRequest request, Guid actingUserId, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, ct)
                   ?? throw new NotFoundException("Role", roleId);

        var names = request.Permissions.Distinct().ToList();
        var permissions = await db.Permissions.Where(p => names.Contains(p.Name)).ToListAsync(ct);
        var unknown = names.Except(permissions.Select(p => p.Name)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException($"Unknown permissions: {string.Join(", ", unknown)}.");
        }

        if (role.Name == SystemRoles.Administrator && !names.Contains(Permissions.UsersManage))
        {
            throw new DomainException(
                $"The {SystemRoles.Administrator} role must keep '{Permissions.UsersManage}' so accounts can always be managed.");
        }

        role.SetPermissions(permissions.Select(p => p.Id));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Permissions of role {Role} set to [{Permissions}] by {ActingUserId}",
            role.Name, string.Join(", ", names.Order()), actingUserId);

        return (await ListAsync(ct)).Single(r => r.Id == roleId);
    }
}
