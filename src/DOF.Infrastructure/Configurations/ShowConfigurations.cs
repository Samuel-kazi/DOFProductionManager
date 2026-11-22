using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class ShowConfiguration : IEntityTypeConfiguration<Show>
{
    public void Configure(EntityTypeBuilder<Show> builder)
    {
        builder.ToTable("shows");
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ShowType).HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.ProducerId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.ProducerId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(s => s.IsArchived);
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("seasons");
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(s => new { s.ShowId, s.SeasonNumber }).IsUnique();
        builder.HasOne(s => s.Show).WithMany().HasForeignKey(s => s.ShowId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(s => s.DisplayName);
    }
}

internal sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("episodes", t =>
        {
            t.HasCheckConstraint("ck_episodes_recording_range",
                "(recording_start IS NULL AND recording_end IS NULL) OR (recording_start IS NOT NULL AND recording_end > recording_start)");
            t.HasCheckConstraint("ck_episodes_episode_number", "episode_number IS NULL OR episode_number > 0");
        });
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.RecordingLocation).HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(e => e.ShowId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ProducerId);
        builder.HasIndex(e => e.RecordingStart);
        builder.HasIndex(e => e.EditingDueDate);
        builder.HasIndex(e => e.ReviewDueDate);
        builder.HasIndex(e => e.ApprovalDueDate);
        // Episode numbers are unique within a season, and within a show's episodes that have no season.
        builder.HasIndex(e => new { e.SeasonId, e.EpisodeNumber }).IsUnique()
            .HasFilter("season_id IS NOT NULL AND episode_number IS NOT NULL");
        builder.HasIndex(e => new { e.ShowId, e.EpisodeNumber }).IsUnique()
            .HasDatabaseName("ix_episodes_show_id_episode_number_without_season")
            .HasFilter("season_id IS NULL AND episode_number IS NOT NULL");
        builder.HasOne(e => e.Show).WithMany().HasForeignKey(e => e.ShowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Season).WithMany().HasForeignKey(e => e.SeasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ProducerId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(e => e.IsCancelled);

        builder.HasMany(e => e.Assignments).WithOne().HasForeignKey(a => a.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EpisodeAssignmentConfiguration : IEntityTypeConfiguration<EpisodeAssignment>
{
    public void Configure(EntityTypeBuilder<EpisodeAssignment> builder)
    {
        builder.ToTable("episode_assignments");
        builder.Property(a => a.Responsibility).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(a => new { a.EpisodeId, a.UserId, a.Responsibility }).IsUnique();
        builder.HasIndex(a => a.UserId);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
