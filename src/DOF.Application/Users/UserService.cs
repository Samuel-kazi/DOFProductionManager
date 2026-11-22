using DOF.Application.Auth;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.Users;

public sealed class UserService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IOptions<OrganisationOptions> organisation,
    TimeProvider clock,
    ILogger<UserService> logger)
{
    public async Task<PagedResponse<UserResponse>> ListAsync(
        int? page, int? pageSize, string? search, bool? isActive, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.Contains(term));
        }

        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(u => u.FullName).ThenBy(u => u.Id))
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResponse<UserResponse>(items, p, size, total);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("User", id);

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await EnsureEmailAvailableAsync(request.Email, exceptUserId: null, ct);
        await EnsureRolesExistAsync(request.RoleIds, ct);

        var user = User.Create(
            request.FullName,
            request.Email,
            hasher.Hash(request.Password),
            request.TimeZone ?? organisation.Value.DefaultTimeZone);
        user.SetRoles(request.RoleIds);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} created", user.Id);
        return await GetAsync(user.Id, ct);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User", id);

        if (user.Version != request.Version)
        {
            throw new ConflictException(
                "This user was modified by someone else. Reload the latest version and try again.");
        }
        // Also guard against a concurrent save landing between this read and our write.
        db.Entry(user).Property(u => u.Version).OriginalValue = request.Version;

        await EnsureEmailAvailableAsync(request.Email, exceptUserId: id, ct);
        await EnsureRolesExistAsync(request.RoleIds, ct);

        var adminRoleId = await GetAdministratorRoleIdAsync(ct);
        if (user.IsActive
            && user.Roles.Any(r => r.RoleId == adminRoleId)
            && !request.RoleIds.Contains(adminRoleId))
        {
            await EnsureAnotherActiveAdministratorAsync(id, adminRoleId, ct);
        }

        user.UpdateProfile(request.FullName, request.Email, request.TimeZone);
        user.SetRoles(request.RoleIds);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} updated", id);
        return await GetAsync(id, ct);
    }

    public async Task<UserResponse> SetStatusAsync(Guid id, bool isActive, Guid actingUserId, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User", id);

        if (user.IsActive == isActive)
        {
            return await GetAsync(id, ct);
        }

        if (isActive)
        {
            user.Activate();
        }
        else
        {
            if (id == actingUserId)
            {
                throw new DomainException("You cannot deactivate your own account.");
            }

            var adminRoleId = await GetAdministratorRoleIdAsync(ct);
            if (user.Roles.Any(r => r.RoleId == adminRoleId))
            {
                await EnsureAnotherActiveAdministratorAsync(id, adminRoleId, ct);
            }

            user.Deactivate();
            await AuthService.RevokeAllForUserAsync(db, id, RefreshToken.Reasons.UserDeactivated, clock.GetUtcNow(), ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} {Action} by {ActingUserId}", id, isActive ? "activated" : "deactivated", actingUserId);
        return await GetAsync(id, ct);
    }

    private static IQueryable<UserResponse> Project(IQueryable<User> query) =>
        query.Select(u => new UserResponse(
            u.Id,
            u.FullName,
            u.Email,
            u.IsActive,
            u.TimeZone,
            u.Roles.OrderBy(r => r.Role.Name).Select(r => new RoleSummaryResponse(r.RoleId, r.Role.Name)).ToList(),
            u.CreatedAt,
            u.UpdatedAt,
            u.Version));

    private async Task EnsureEmailAvailableAsync(string email, Guid? exceptUserId, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.Email == normalized && u.Id != exceptUserId, ct))
        {
            throw new ConflictException($"A user with email '{normalized}' already exists.");
        }
    }

    private async Task EnsureRolesExistAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var distinct = roleIds.Distinct().ToList();
        var found = await db.Roles.CountAsync(r => distinct.Contains(r.Id), ct);
        if (found != distinct.Count)
        {
            throw new DomainException("One or more roles do not exist.");
        }
    }

    private async Task<Guid> GetAdministratorRoleIdAsync(CancellationToken ct) =>
        await db.Roles.Where(r => r.Name == SystemRoles.Administrator).Select(r => r.Id).SingleAsync(ct);

    private async Task EnsureAnotherActiveAdministratorAsync(Guid userId, Guid adminRoleId, CancellationToken ct)
    {
        var others = await db.Users.CountAsync(
            u => u.Id != userId && u.IsActive && u.Roles.Any(r => r.RoleId == adminRoleId), ct);
        if (others == 0)
        {
            throw new DomainException("At least one active Administrator must remain.");
        }
    }
}
