using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Responses;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Auth;

/// <summary>Read-side lookups for the signed-in user's identity, roles and effective permissions.</summary>
public sealed class UserProfileQueries(IApplicationDbContext db)
{
    public async Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.TimeZone,
                Roles = u.Roles.Select(r => r.Role.Name).ToList(),
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("User", userId);

        var permissions = await GetPermissionNamesAsync(userId, ct);

        return new CurrentUserResponse(user.Id, user.FullName, user.Email, user.TimeZone,
            [.. user.Roles.Order()], permissions);
    }

    /// <summary>
    /// Effective permissions of an active user, or <c>null</c> if the user no longer exists or is
    /// deactivated. Called on every authenticated request so changes take effect immediately.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetActiveUserPermissionsAsync(Guid userId, CancellationToken ct)
    {
        var isActive = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsActive)
            .SingleOrDefaultAsync(ct);

        return isActive == true ? await GetPermissionNamesAsync(userId, ct) : null;
    }

    private async Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.Permissions.Select(rp => rp.Permission.Name))
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);
}
