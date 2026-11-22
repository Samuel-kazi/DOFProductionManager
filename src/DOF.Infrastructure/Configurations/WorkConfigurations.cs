using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ProjectType).HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ManagerId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ManagerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Members).WithOne().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.Property(m => m.ProjectRole).HasMaxLength(60).IsRequired();
        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProjectMilestoneConfiguration : IEntityTypeConfiguration<ProjectMilestone>
{
    public void Configure(EntityTypeBuilder<ProjectMilestone> builder)
    {
        builder.ToTable("project_milestones");
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(m => m.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(m => m.ProjectId);
        builder.HasIndex(m => m.DueDate);
        builder.HasIndex(m => m.AssignedTo);
        builder.HasOne(m => m.Project).WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.AssignedTo).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => t.DueAt);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.ProjectId);
        builder.HasIndex(t => t.EpisodeId);
        builder.ToTable(t => t.HasCheckConstraint("ck_tasks_project_or_episode", "project_id IS NULL OR episode_id IS NULL"));
        builder.HasOne(t => t.Episode).WithMany().HasForeignKey(t => t.EpisodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CompletedBy).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(t => t.PrimaryAssigneeId);

        builder.HasMany(t => t.Assignments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignments");
        builder.HasIndex(a => new { a.TaskId, a.UserId }).IsUnique();
        builder.HasIndex(a => a.UserId);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");
        builder.Property(a => a.EntityType).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(40).IsRequired();
        builder.Property(a => a.OldValues).HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.CreatedAt });
        builder.HasIndex(a => a.CreatedAt);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
