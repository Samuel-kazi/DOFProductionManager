using System.Net;
using System.Net.Http.Json;
using DOF.Application.GoogleCalendar;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using DOF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

/// <summary>Weekly session series (devotions): a week planned at once, a theme per day and who is sharing.</summary>
public class SessionWeekTests(DofApiFactory factory)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static readonly DateOnly Monday = new(2035, 3, 5);

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static SessionDayRequest Day(DateOnly monday, int offset, string? theme, string? speaker = null, Guid? eventId = null) =>
        new(At(monday.AddDays(offset), 6), At(monday.AddDays(offset), 7), theme, speaker, eventId);

    private static SessionWeekRequest Week(string name, DateOnly monday, IReadOnlyList<SessionDayRequest> days, string? speaker = "Pastor John",
        int? version = null) =>
        new(name, monday, "MinistryEvent", "Chapel", "https://meet.example.org/devotion", speaker, days, version);

    [Fact]
    public async Task A_devotion_week_puts_each_day_on_the_calendar_with_its_theme_and_who_is_sharing()
    {
        var admin = await factory.AdminClientAsync();
        var name = $"Morning Devotion {Guid.NewGuid():N}"[..24];
        var response = await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks", Week(name, Monday,
        [
            Day(Monday, 0, "Walking in faith"), Day(Monday, 1, "The power of prayer"), Day(Monday, 2, "Grace that saves", "Rev. Mary"),
            Day(Monday, 3, "Love your neighbour"), Day(Monday, 4, null),
        ]), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var week = await response.DataAsync<SessionWeekResponse>();
        Assert.Equal(5, week.Days.Count);
        Assert.Equal($"{name}: Walking in faith", week.Days[0].Title);
        Assert.Equal(name, week.Days[4].Title); // no theme yet
        Assert.Null(week.Days[0].Speaker);       // same as the week
        Assert.Equal("Rev. Mary", week.Days[2].Speaker);

        // Each day is an ordinary event: on the calendar with who is sharing, and in the event details.
        var items = await (await admin.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(At(Monday, 0).ToString("O"))}&end={Uri.EscapeDataString(At(Monday.AddDays(7), 0).ToString("O"))}&sourceTypes=CalendarEvent", Ct))
            .DataAsync<List<CalendarItemResponse>>();
        var mine = items.Where(i => i.Title.StartsWith(name)).OrderBy(i => i.StartAt).ToList();
        Assert.Equal(["Pastor John", "Pastor John", "Rev. Mary", "Pastor John", "Pastor John"], mine.Select(i => i.Speaker));
        Assert.All(mine, i => Assert.Equal("Chapel", i.Location));
        var wednesday = await (await admin.GetAsync($"/api/v1/calendar/events/{week.Days[2].EventId}", Ct)).DataAsync<CalendarEventResponse>();
        Assert.Equal(("Rev. Mary", week.Id, "Grace that saves"), (wednesday.Speaker, wednesday.SessionWeekId, wednesday.Theme));

        // Listed for copying next week.
        var earlier = await (await admin.GetAsync($"/api/v1/calendar/session-weeks?name={Uri.EscapeDataString(name.ToUpperInvariant())}&before=2035-03-12", Ct))
            .DataAsync<List<SessionWeekSummaryResponse>>();
        Assert.Equal(week.Id, Assert.Single(earlier).Id);
    }

    [Fact]
    public async Task Editing_the_week_changes_every_day_adds_new_ones_and_cancels_days_left_out()
    {
        var admin = await factory.AdminClientAsync();
        var name = $"Evening Prayer {Guid.NewGuid():N}"[..22];
        var monday = Monday.AddDays(14);
        var week = await (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week(name, monday, [Day(monday, 0, "Hope"), Day(monday, 1, "Joy"), Day(monday, 2, "Peace")]), Ct)).DataAsync<SessionWeekResponse>();

        // Someone else shares the whole week, Tuesday is dropped, Thursday is added and Monday's theme changes.
        var edited = await admin.PutAsJsonAsync($"/api/v1/calendar/session-weeks/{week.Id}", Week(name, monday,
        [
            Day(monday, 0, "Living hope", eventId: week.Days[0].EventId), Day(monday, 2, "Peace", eventId: week.Days[2].EventId),
            Day(monday, 3, "Faith"),
        ], speaker: "Sister Ann", version: week.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var after = await edited.DataAsync<SessionWeekResponse>();
        Assert.Equal([$"{name}: Living hope", $"{name}: Peace", $"{name}: Faith"], after.Days.Select(d => d.Title));
        Assert.Equal(week.Days[0].EventId, after.Days[0].EventId); // the same event, not a copy

        var tuesday = await (await admin.GetAsync($"/api/v1/calendar/events/{week.Days[1].EventId}", Ct)).DataAsync<CalendarEventResponse>();
        Assert.Equal("Cancelled", tuesday.Status);
        var monday1 = await (await admin.GetAsync($"/api/v1/calendar/events/{after.Days[0].EventId}", Ct)).DataAsync<CalendarEventResponse>();
        Assert.Equal("Sister Ann", monday1.Speaker);

        // A stale version is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/calendar/session-weeks/{week.Id}",
            Week(name, monday, [Day(monday, 0, "x", eventId: week.Days[0].EventId)], version: week.Version), Ct)).StatusCode);
    }

    [Fact]
    public async Task Weeks_are_checked_and_need_calendar_permission()
    {
        var admin = await factory.AdminClientAsync();
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var monday = Monday.AddDays(28);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week("Devotion", monday, [Day(monday, 0, "Hope")]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week("Devotion", monday.AddDays(1), [Day(monday, 1, "Hope")]), Ct)).StatusCode); // not a Monday
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week("Devotion", monday, []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week("Devotion", monday, [Day(monday, 8, "Next week")]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks",
            Week("Devotion", monday, [Day(monday, 0, "A"), Day(monday, 0, "B")]), Ct)).StatusCode);
    }

    [Fact]
    public async Task One_google_invite_covers_the_whole_week_and_follows_its_changes()
    {
        var admin = await factory.AdminClientAsync();
        await GoogleCalendarTests.ConnectAsync(factory, admin);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var name = $"Week Devotion {Guid.NewGuid():N}"[..21];
        var monday = Monday.AddDays(42);
        var week = await (await admin.PostAsJsonAsync("/api/v1/calendar/session-weeks", Week(name, monday,
        [
            Day(monday, 0, "Hope"), Day(monday, 1, "Joy"), Day(monday, 2, "Peace", "Rev. Mary"), Day(monday, 3, "Faith"), Day(monday, 4, "Love"),
        ]), Ct)).DataAsync<SessionWeekResponse>();
        var path = $"/api/v1/calendar/session-weeks/{week.Id}/google-invites";
        Assert.Contains("\"data\":null", await (await admin.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct));

        var sent = await admin.PutAsJsonAsync(path, new SendGoogleInvitesRequest([member.Id], ["guest@example.org"]), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var invite = await sent.DataAsync<GoogleInviteResponse>();
        Assert.Equal(("Synced", week.Id, (Guid?)null), (invite.Status, invite.SessionWeekId, invite.CalendarEventId));

        // One repeating Google event for Monday to Friday at 06:00, each day with its theme and speaker.
        var googleId = await factory.InScopeAsync(sp => sp.GetRequiredService<DofDbContext>().GoogleCalendarInvites
            .Where(i => i.SessionWeekId == week.Id).Select(i => i.GoogleEventId).SingleAsync(Ct));
        var data = factory.Google.Events[googleId];
        Assert.Equal(name, data.Summary);
        Assert.Equal(At(monday, 6), data.Start.DateTime);
        Assert.Equal(At(monday, 7), data.End.DateTime);
        var rule = Assert.Single(data.Recurrence!);
        Assert.Equal($"RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;UNTIL={At(monday.AddDays(4), 6).UtcDateTime:yyyyMMdd'T'HHmmss'Z'}", rule);
        Assert.Equal([$"{name}: Hope", $"{name}: Joy", $"{name}: Peace", $"{name}: Faith", $"{name}: Love"], data.Instances!.Select(i => i.Summary));
        Assert.StartsWith("Sharing: Rev. Mary", data.Instances![2].Description);
        Assert.Contains("Peace · Sharing: Rev. Mary", data.Description);
        Assert.Equal(2, data.Attendees.Count);

        // Dropping Friday and changing a theme updates the same Google event once.
        await admin.PutAsJsonAsync($"/api/v1/calendar/session-weeks/{week.Id}", Week(name, monday,
        [
            Day(monday, 0, "Living hope", eventId: week.Days[0].EventId), Day(monday, 1, "Joy", eventId: week.Days[1].EventId),
            Day(monday, 2, "Peace", "Rev. Mary", week.Days[2].EventId), Day(monday, 3, "Faith", eventId: week.Days[3].EventId),
        ], version: week.Version), Ct);
        var before = factory.Google.CallsFor(googleId).Count;
        await factory.InScopeAsync(sp => sp.GetRequiredService<GoogleInviteSync>().RunAsync(DateTimeOffset.UtcNow, Ct));
        await factory.InScopeAsync(sp => sp.GetRequiredService<GoogleInviteSync>().RunAsync(DateTimeOffset.UtcNow, Ct));
        Assert.Equal(["Update"], factory.Google.CallsFor(googleId).Skip(before).Select(c => c.Kind).Where(k => k != "Get"));
        data = factory.Google.Events[googleId];
        Assert.Contains("BYDAY=MO,TU,WE,TH;", data.Recurrence![0]);
        Assert.Equal($"{name}: Living hope", data.Instances![0].Summary);

        // Event details for a day point to the week's invite; removing takes the series off Google.
        var day = await (await admin.GetAsync($"/api/v1/calendar/events/{week.Days[0].EventId}", Ct)).DataAsync<CalendarEventResponse>();
        Assert.Equal(week.Id, day.SessionWeekId);
        var removed = await (await admin.DeleteAsync(path, Ct)).DataAsync<GoogleInviteResponse>();
        Assert.Equal("Removed", removed.Status);
        Assert.False(factory.Google.Events.ContainsKey(googleId));
    }
}
