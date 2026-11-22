using DOF.Application.Common;
using DOF.Contracts.Common;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class CalendarDomainTests
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static readonly DateTimeOffset Nine = new(2031, 1, 6, 9, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Meeting() =>
        CalendarEvent.Create("Staff meeting", null, CalendarEventType.Meeting, Nine, Nine.AddHours(1), false, null, null, Priority.Normal);

    [Fact]
    public void Events_must_end_after_they_start()
    {
        Assert.Throws<DomainException>(() =>
            CalendarEvent.Create("x", null, CalendarEventType.Meeting, Nine, Nine, false, null, null, Priority.Normal));
        Assert.Throws<DomainException>(() => Meeting().Reschedule(Nine, Nine.AddMinutes(-5)));
    }

    [Fact]
    public void Cancelled_events_cannot_be_rescheduled()
    {
        var meeting = Meeting();
        meeting.Cancel();

        Assert.Equal(CalendarEventStatus.Cancelled, meeting.Status);
        Assert.Throws<DomainException>(() => meeting.Reschedule(Nine.AddDays(1), Nine.AddDays(1).AddHours(1)));
    }

    [Fact]
    public void Assignments_are_unique_per_person_and_organizer_wins()
    {
        var meeting = Meeting();
        Guid ada = Guid.NewGuid(), ben = Guid.NewGuid();

        meeting.SetAssignments([(ada, CalendarAssignmentType.Attendee), (ada, CalendarAssignmentType.Organizer), (ben, CalendarAssignmentType.Attendee)]);
        meeting.SetAssignments([(ada, CalendarAssignmentType.Organizer)]);

        var only = Assert.Single(meeting.Assignments);
        Assert.Equal(ada, only.UserId);
        Assert.Equal(CalendarAssignmentType.Organizer, only.AssignmentType);
    }

    [Fact]
    public void Text_fields_are_trimmed_and_blank_values_cleared()
    {
        var meeting = CalendarEvent.Create("  Retreat  ", "   ", CalendarEventType.MinistryEvent, Nine, Nine.AddHours(2),
            false, " Naivasha ", "", Priority.High);

        Assert.Equal("Retreat", meeting.Title);
        Assert.Null(meeting.Description);
        Assert.Equal("Naivasha", meeting.Location);
        Assert.Null(meeting.MeetingUrl);
    }

    [Fact]
    public void Days_start_at_local_midnight()
    {
        Assert.Equal(new DateTimeOffset(2031, 4, 9, 21, 0, 0, TimeSpan.Zero), ZonedTime.StartOfDay(new DateOnly(2031, 4, 10), Nairobi));
        Assert.Equal(new DateOnly(2031, 4, 10), ZonedTime.LocalDate(new DateTimeOffset(2031, 4, 9, 22, 30, 0, TimeSpan.Zero), Nairobi));
    }

    [Theory]
    [InlineData("2031-04-07", "2031-04-07")] // Monday
    [InlineData("2031-04-10", "2031-04-07")] // Thursday
    [InlineData("2031-04-13", "2031-04-07")] // Sunday
    public void Weeks_start_on_monday(string date, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), ZonedTime.StartOfWeek(DateOnly.Parse(date)));

    [Fact]
    public void All_day_spans_snap_to_whole_local_days_and_are_stable_when_saved_again()
    {
        var (start, end) = ZonedTime.SnapToWholeDays(
            new DateTimeOffset(2031, 6, 10, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2031, 6, 10, 10, 0, 0, TimeSpan.Zero), Nairobi);

        Assert.Equal(new DateTimeOffset(2031, 6, 9, 21, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2031, 6, 10, 21, 0, 0, TimeSpan.Zero), end);
        Assert.Equal((start, end), ZonedTime.SnapToWholeDays(start, end, Nairobi));
        Assert.Throws<DomainException>(() => ZonedTime.SnapToWholeDays(start, start.AddDays(-2), Nairobi));
    }

    [Fact]
    public void Shared_values_match_domain_enums()
    {
        Assert.Equal(Enum.GetNames<CalendarEventType>(), CalendarValues.EventTypes);
        Assert.Equal(Enum.GetNames<CalendarEventStatus>(), CalendarValues.Statuses);
        Assert.Equal(Enum.GetNames<Priority>(), CalendarValues.Priorities);
        Assert.Equal(Enum.GetNames<CalendarAssignmentType>(), CalendarValues.AssignmentTypes);
        Assert.Equal(Enum.GetNames<CalendarSourceType>(), CalendarValues.SourceTypes);
    }
}
