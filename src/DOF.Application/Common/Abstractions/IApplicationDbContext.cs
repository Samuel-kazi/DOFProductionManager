using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DOF.Application.Common.Abstractions;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<CalendarEvent> CalendarEvents { get; }
    DbSet<CalendarEventAssignment> CalendarEventAssignments { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<ProjectMilestone> ProjectMilestones { get; }
    DbSet<WorkTask> Tasks { get; }
    DbSet<TaskAssignment> TaskAssignments { get; }
    DbSet<ActivityLog> ActivityLogs { get; }
    DbSet<Show> Shows { get; }
    DbSet<Season> Seasons { get; }
    DbSet<Episode> Episodes { get; }
    DbSet<EpisodeAssignment> EpisodeAssignments { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
