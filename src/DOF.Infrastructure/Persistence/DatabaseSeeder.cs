using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Infrastructure.Persistence;

/// <summary>
/// Idempotent reference-data seeding, run at every startup:
/// adds missing permissions and system roles, grants defaults only to newly created items (so changes
/// made by administrators are preserved), and creates the bootstrap Administrator if there are no users.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DofDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSeeder));

        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        if (pending.Any())
        {
            logger.LogWarning("Skipping seeding: {Count} migration(s) have not been applied", pending.Count());
            return;
        }

        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Name, ct);
        var newPermissions = new HashSet<string>();
        foreach (var (name, description) in Permissions.All)
        {
            if (!permissions.ContainsKey(name))
            {
                var permission = new Permission(name, description);
                db.Permissions.Add(permission);
                permissions[name] = permission;
                newPermissions.Add(name);
            }
        }

        var roles = await db.Roles.Include(r => r.Permissions).ToDictionaryAsync(r => r.Name, ct);
        foreach (var definition in SystemRoles.All)
        {
            var isNewRole = !roles.TryGetValue(definition.Name, out var role);
            if (isNewRole)
            {
                role = new Role(definition.Name, definition.Description);
                db.Roles.Add(role);
                roles[definition.Name] = role;
            }

            foreach (var permissionName in definition.DefaultPermissions)
            {
                if (isNewRole || newPermissions.Contains(permissionName))
                {
                    role!.Grant(permissions[permissionName].Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        if (newPermissions.Count > 0)
        {
            logger.LogInformation("Seeded permissions: {Permissions}", string.Join(", ", newPermissions));
        }

        await SeedChannelsAsync(db, logger, ct);
        await CreateBootstrapAdministratorAsync(sp, db, roles[SystemRoles.Administrator], logger, ct);
    }

    /// <summary>The specification's channel list. Added once by name; later edits and deactivations are kept.</summary>
    internal static readonly (string Name, ChannelType Type)[] DefaultChannels =
    [
        ("YouTube", ChannelType.Video), ("Spotify", ChannelType.Audio), ("Facebook", ChannelType.Social),
        ("Instagram", ChannelType.Social), ("Television", ChannelType.Broadcast), ("Website", ChannelType.Web),
        ("Other", ChannelType.Other),
    ];

    private static async Task SeedChannelsAsync(DofDbContext db, ILogger logger, CancellationToken ct)
    {
        var existing = (await db.DistributionChannels.Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = DefaultChannels.Where(c => !existing.Contains(c.Name)).ToList();
        foreach (var (name, type) in added)
        {
            db.DistributionChannels.Add(DistributionChannel.Create(name, type, null));
        }
        if (added.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded distribution channels: {Channels}", string.Join(", ", added.Select(c => c.Name)));
        }
    }

    private static async Task CreateBootstrapAdministratorAsync(
        IServiceProvider sp, DofDbContext db, Role administrator, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var bootstrap = sp.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        if (string.IsNullOrWhiteSpace(bootstrap.AdminEmail) || string.IsNullOrWhiteSpace(bootstrap.AdminPassword))
        {
            logger.LogWarning(
                "No users exist and no bootstrap administrator is configured (Bootstrap:AdminEmail / Bootstrap:AdminPassword)");
            return;
        }

        if (bootstrap.AdminPassword.Length < ValidationRules.MinPasswordLength)
        {
            throw new InvalidOperationException(
                $"Bootstrap:AdminPassword must be at least {ValidationRules.MinPasswordLength} characters.");
        }

        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var organisation = sp.GetRequiredService<IOptions<OrganisationOptions>>().Value;
        var admin = User.Create(bootstrap.AdminFullName, bootstrap.AdminEmail,
            hasher.Hash(bootstrap.AdminPassword), organisation.DefaultTimeZone);
        admin.SetRoles([administrator.Id]);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created bootstrap administrator {Email}", admin.Email);
    }
}
