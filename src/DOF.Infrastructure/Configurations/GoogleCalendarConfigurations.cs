using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DOF.Infrastructure.Configurations;

internal sealed class GoogleCalendarConnectionConfiguration : IEntityTypeConfiguration<GoogleCalendarConnection>
{
    public void Configure(EntityTypeBuilder<GoogleCalendarConnection> builder)
    {
        builder.ToTable("google_calendar_connections");
        builder.Property(c => c.AccountEmail).HasMaxLength(320).IsRequired();
        builder.Property(c => c.EncryptedRefreshToken).IsRequired();
        builder.Property(c => c.CalendarId).HasMaxLength(320).IsRequired();
        builder.Property(c => c.LastError).HasMaxLength(1000);
        builder.Ignore(c => c.IsActive);
        // At most one active connection for the organisation.
        builder.HasIndex(c => c.ArchivedAt).IsUnique().HasFilter("archived_at IS NULL")
            .HasDatabaseName("ix_google_calendar_connections_single_active");
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ConnectedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GoogleCalendarInviteConfiguration : IEntityTypeConfiguration<GoogleCalendarInvite>
{
    public void Configure(EntityTypeBuilder<GoogleCalendarInvite> builder)
    {
        builder.ToTable("google_calendar_invites");
        builder.Property(i => i.GoogleEventId).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.SyncedHash).HasMaxLength(64);
        builder.Property(i => i.LastError).HasMaxLength(1000);
        builder.HasIndex(i => i.CalendarEventId).IsUnique();
        builder.HasIndex(i => new { i.Status, i.NextAttemptAt });
        builder.HasOne<CalendarEvent>().WithMany().HasForeignKey(i => i.CalendarEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoogleCalendarConnection>().WithMany().HasForeignKey(i => i.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Guests).WithOne().HasForeignKey(g => g.InviteId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Guests).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class GoogleCalendarInviteGuestConfiguration : IEntityTypeConfiguration<GoogleCalendarInviteGuest>
{
    public void Configure(EntityTypeBuilder<GoogleCalendarInviteGuest> builder)
    {
        builder.ToTable("google_calendar_invite_guests");
        builder.Property(g => g.Email).HasMaxLength(320).IsRequired();
        builder.Property(g => g.DisplayName).HasMaxLength(200);
        builder.Property(g => g.Response).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(g => new { g.InviteId, g.Email }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
