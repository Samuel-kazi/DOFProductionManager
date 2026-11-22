using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DOF.Infrastructure.Persistence;

public sealed class DofDbContext(DbContextOptions<DofDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DofDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IVersioned).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IVersioned.Version))
                    .IsConcurrencyToken();
            }
        }
    }
}
