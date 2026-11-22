using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders");
        builder.Property(r => r.SourceType).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.Channel).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.CancellationReason).HasMaxLength(200);
        // The dispatcher's query: due pending reminders.
        builder.HasIndex(r => new { r.Status, r.ScheduledAt });
        builder.HasIndex(r => new { r.SourceType, r.SourceId });
        builder.HasIndex(r => new { r.UserId, r.Status });
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(r => r.IsPending);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.NotificationType).HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.SourceType).HasConversion<string>().HasMaxLength(40);
        builder.Property(n => n.IdempotencyKey).HasMaxLength(300).IsRequired();
        builder.HasIndex(n => n.IdempotencyKey).IsUnique();
        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("notification_outbox");
        builder.Property(m => m.EventType).HasMaxLength(60).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.IdempotencyKey).HasMaxLength(300).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(m => m.IdempotencyKey).IsUnique();
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property<List<int>>("_offsets").HasColumnName("reminder_offsets").HasColumnType("integer[]");
        builder.Ignore(p => p.ReminderOffsets);
    }
}
