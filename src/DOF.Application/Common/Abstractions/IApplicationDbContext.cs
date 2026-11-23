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
    DbSet<DistributionChannel> DistributionChannels { get; }
    DbSet<Release> Releases { get; }
    DbSet<Reminder> Reminders { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<GoogleCalendarConnection> GoogleCalendarConnections { get; }
    DbSet<GoogleCalendarInvite> GoogleCalendarInvites { get; }
    DbSet<GoogleCalendarInviteGuest> GoogleCalendarInviteGuests { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>Stops tracking everything (used by background jobs to recover cleanly after a failed save).</summary>
    void ClearTracked();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
