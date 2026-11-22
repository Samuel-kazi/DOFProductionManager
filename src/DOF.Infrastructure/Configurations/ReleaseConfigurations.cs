using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class DistributionChannelConfiguration : IEntityTypeConfiguration<DistributionChannel>
{
    public void Configure(EntityTypeBuilder<DistributionChannel> builder)
    {
        builder.ToTable("distribution_channels");
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ChannelType).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("releases", t =>
        {
            // A published release always records when it actually went out.
            t.HasCheckConstraint("ck_releases_published_has_time", "status <> 'Published' OR actual_published_at IS NOT NULL");
            // "Overdue" is derived when reading, never stored.
            t.HasCheckConstraint("ck_releases_status_not_overdue", "status <> 'Overdue'");
        });
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.ExternalUrl).HasMaxLength(2000);
        builder.HasIndex(r => r.EpisodeId);
        builder.HasIndex(r => r.ScheduledAt);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.AssignedTo);
        builder.HasIndex(r => r.ProjectId);
        // One live release per episode and channel; cancelled ones stay for history.
        builder.HasIndex(r => new { r.EpisodeId, r.DistributionChannelId }).IsUnique().HasFilter("status <> 'Cancelled'");
        builder.HasOne(r => r.Episode).WithMany().HasForeignKey(r => r.EpisodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Channel).WithMany().HasForeignKey(r => r.DistributionChannelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.AssignedTo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.PublishedBy).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.IsClosed);
    }
}
