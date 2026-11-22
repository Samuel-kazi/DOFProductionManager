using DOF.Contracts.Common;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class ReminderDomainTests
{
    private static readonly DateTimeOffset Anchor = new(2034, 12, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Value_lists_match_the_domain_enums()
    {
        Assert.Equal(Enum.GetNames<ReminderSourceType>(), ReminderValues.SourceTypes);
        Assert.Equal(Enum.GetNames<ReminderChannel>(), ReminderValues.Channels);
        Assert.Equal(Enum.GetNames<ReminderStatus>(), ReminderValues.Statuses);
        Assert.Equal(Enum.GetNames<NotificationType>(), ReminderValues.NotificationTypes);
        Assert.Equal(Reminder.StandardOffsets, ReminderValues.StandardOffsets);
    }

    [Theory]
    [InlineData(0, "At the time")]
    [InlineData(15, "15 minutes before")]
    [InlineData(60, "1 hour before")]
    [InlineData(1440, "1 day before")]
    [InlineData(4320, "3 days before")]
    [InlineData(10080, "7 days before")]
    public void Standard_offsets_have_readable_labels(int minutes, string label) =>
        Assert.Equal(label, ReminderValues.OffsetLabel(minutes));

    [Fact]
    public void Offset_reminders_count_back_from_the_record_and_follow_it()
    {
        var reminder = Reminder.ForOffset(Guid.NewGuid(), ReminderSourceType.Task, Guid.NewGuid(), 60, Anchor, ReminderChannel.InApp, true);
        Assert.Equal(Anchor.AddHours(-1), reminder.ScheduledAt);
        Assert.Equal(Anchor.AddDays(1).AddHours(-1), reminder.ScheduleFor(Anchor.AddDays(1)));

        var fixedTime = Reminder.AtTime(Guid.NewGuid(), ReminderSourceType.Task, Guid.NewGuid(), Anchor.AddDays(-2), ReminderChannel.InApp);
        Assert.Equal(Anchor.AddDays(-2), fixedTime.ScheduleFor(Anchor.AddDays(5)));
        Assert.Throws<DomainException>(() =>
            Reminder.ForOffset(Guid.NewGuid(), ReminderSourceType.Task, Guid.NewGuid(), -5, Anchor, ReminderChannel.InApp, false));
    }

    [Fact]
    public void Only_pending_reminders_change_or_cancel_and_delivered_ones_are_kept()
    {
        var reminder = Reminder.ForOffset(Guid.NewGuid(), ReminderSourceType.Task, Guid.NewGuid(), 60, Anchor, ReminderChannel.InApp, false);
        reminder.Delivered(Anchor);
        reminder.Cancel("Too late");
        Assert.Equal(ReminderStatus.Delivered, reminder.Status);
        Assert.Throws<DomainException>(() => reminder.Change(15, null, Anchor, ReminderChannel.InApp));
    }

    [Fact]
    public void Failed_attempts_count_up_and_the_last_one_is_final()
    {
        var reminder = Reminder.ForOffset(Guid.NewGuid(), ReminderSourceType.Task, Guid.NewGuid(), 60, Anchor, ReminderChannel.Email, true);
        reminder.AwaitDelivery(Anchor);
        reminder.RecordAttemptFailure("down", final: false);
        Assert.Equal((ReminderStatus.Processing, 1), (reminder.Status, reminder.RetryCount));
        reminder.RecordAttemptFailure("still down", final: true);
        Assert.Equal((ReminderStatus.Failed, 2, "still down"), (reminder.Status, reminder.RetryCount, reminder.LastError));
    }

    [Fact]
    public void Outbox_retries_back_off_then_fail()
    {
        var message = new OutboxMessage(OutboxMessage.SendEmail, "{}", "k", Anchor);
        Assert.False(message.RecordFailure("x", Anchor, maxAttempts: 3));
        Assert.Equal((OutboxStatus.Pending, Anchor.AddMinutes(1)), (message.Status, message.NextAttemptAt));
        Assert.False(message.RecordFailure("x", Anchor, maxAttempts: 3));
        Assert.Equal(Anchor.AddMinutes(5), message.NextAttemptAt);
        Assert.True(message.RecordFailure("x", Anchor, maxAttempts: 3));
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(TimeSpan.FromHours(4), OutboxMessage.RetryDelay(9));
    }

    [Fact]
    public void Preferences_keep_sorted_unique_offsets_within_limits()
    {
        var pref = new NotificationPreference(Guid.NewGuid());
        Assert.Equal(NotificationPreference.DefaultOffsets, pref.ReminderOffsets);
        pref.Update(true, false, true, [1440, 60, 60]);
        Assert.Equal([60, 1440], pref.ReminderOffsets);
        Assert.Throws<DomainException>(() => pref.Update(false, false, false, [1, 2, 3, 4, 5, 6, 7]));
        Assert.Throws<DomainException>(() => pref.Update(false, false, false, [-1]));
    }

    [Fact]
    public void Notifications_are_read_once()
    {
        var n = new Notification(Guid.NewGuid(), "T", "M", NotificationType.Reminder, ReminderSourceType.Task, Guid.NewGuid(), "k");
        n.MarkRead(Anchor);
        n.MarkRead(Anchor.AddHours(1));
        Assert.Equal(Anchor, n.ReadAt);
    }
}
