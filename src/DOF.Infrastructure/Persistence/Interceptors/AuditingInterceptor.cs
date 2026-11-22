using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DOF.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Maintains <c>created_at</c>, <c>updated_at</c>, <c>created_by</c>, <c>updated_by</c> and the
/// optimistic-concurrency <c>version</c> so individual services never have to remember to.
/// Scoped, because it reads the signed-in user of the current request.
/// </summary>
public sealed class AuditingInterceptor(TimeProvider clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    if (entry.Entity is AuditableEntity)
                    {
                        entry.Property(nameof(AuditableEntity.CreatedBy)).CurrentValue = userId;
                        entry.Property(nameof(AuditableEntity.UpdatedBy)).CurrentValue = userId;
                    }
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    if (entry.Entity is AuditableEntity)
                    {
                        entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                        entry.Property(nameof(AuditableEntity.UpdatedBy)).CurrentValue = userId;
                    }
                    if (entry.Entity is IVersioned versioned)
                    {
                        entry.Property(nameof(IVersioned.Version)).CurrentValue = versioned.Version + 1;
                    }
                    break;
            }
        }
    }
}
