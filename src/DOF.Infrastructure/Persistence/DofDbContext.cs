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
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<CalendarEventAssignment> CalendarEventAssignments => Set<CalendarEventAssignment>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectMilestone> ProjectMilestones => Set<ProjectMilestone>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Show> Shows => Set<Show>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<EpisodeAssignment> EpisodeAssignments => Set<EpisodeAssignment>();
    public DbSet<DistributionChannel> DistributionChannels => Set<DistributionChannel>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<GoogleCalendarConnection> GoogleCalendarConnections => Set<GoogleCalendarConnection>();
    public DbSet<GoogleCalendarInvite> GoogleCalendarInvites => Set<GoogleCalendarInvite>();
    public DbSet<GoogleCalendarInviteGuest> GoogleCalendarInviteGuests => Set<GoogleCalendarInviteGuest>();

    public void ClearTracked() => ChangeTracker.Clear();

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

            // created_by / updated_by reference users; deleting a user never cascades into operational records.
            if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var entity = modelBuilder.Entity(entityType.ClrType);
                entity.HasOne(typeof(User)).WithMany().HasForeignKey(nameof(AuditableEntity.CreatedBy)).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(typeof(User)).WithMany().HasForeignKey(nameof(AuditableEntity.UpdatedBy)).OnDelete(DeleteBehavior.Restrict);
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
