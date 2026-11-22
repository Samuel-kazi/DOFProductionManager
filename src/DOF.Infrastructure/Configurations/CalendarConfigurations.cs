using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("calendar_events");
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);

        // Range queries filter on start_at < end AND end_at > start.
        builder.HasIndex(e => new { e.StartAt, e.EndAt });
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Assignments)
            .WithOne()
            .HasForeignKey(a => a.CalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CalendarEventAssignmentConfiguration : IEntityTypeConfiguration<CalendarEventAssignment>
{
    public void Configure(EntityTypeBuilder<CalendarEventAssignment> builder)
    {
        builder.ToTable("calendar_event_assignments");
        builder.Property(a => a.AssignmentType).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(a => new { a.CalendarEventId, a.UserId }).IsUnique();
        builder.HasIndex(a => a.UserId);
        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
