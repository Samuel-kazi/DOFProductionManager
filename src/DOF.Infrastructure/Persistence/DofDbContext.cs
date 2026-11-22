using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DOF.Infrastructure.Persistence;

public sealed class DofDbContext(DbContextOptions<DofDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DofDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Ids are assigned by the domain (UUIDv7) when an entity is constructed. Telling EF they are never
            // database-generated makes it treat new child entities added through navigations as inserts.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            if (typeof(IVersioned).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IVersioned.Version))
                    .IsConcurrencyToken();
            }
        }
    }
}
