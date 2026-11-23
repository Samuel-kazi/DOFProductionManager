using System.Net;
using System.Net.Http.Json;
using System.Text;
using DOF.Application.Calendar;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class ScheduleExportTests(DofApiFactory factory)
{
    // A far-future year of its own, so other tests' events never appear.
    private static readonly DateOnly Monday = new(2033, 6, 6);
    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(3));

    private static async Task<CalendarEventResponse> EventAsync(HttpClient client, string title, DateTimeOffset start, DateTimeOffset end,
        bool allDay = false, string priority = "Normal", params CalendarAssignmentRequest[] people) =>
        await (await client.PostAsJsonAsync("/api/v1/calendar/events",
            new CreateCalendarEventRequest(title, null, "Meeting", start, end, allDay, "Studio A", null, priority, people), Ct))
            .DataAsync<CalendarEventResponse>();

    [Fact]
    public async Task Each_period_downloads_as_a_pdf_named_after_its_range()
    {
        var admin = await factory.AdminClientAsync();
        await EventAsync(admin, "Week planning", At(Monday, 9), At(Monday, 10));

        foreach (var (period, name) in new[]
                 {
                     ("Day", "dof-schedule-day-2033-06-08.pdf"), ("Week", "dof-schedule-week-2033-06-06.pdf"),
                     ("Month", "dof-schedule-month-2033-06-01.pdf"), ("Year", "dof-schedule-year-2033-01-01.pdf"),
                 })
        {
            var response = await admin.GetAsync($"/api/v1/calendar/export?period={period}&date=2033-06-08", Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(name, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
            var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    [Fact]
    public async Task The_schedule_holds_the_periods_items_split_by_day_and_respects_filters()
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        var week = Monday.AddDays(14);
        await EventAsync(admin, "Mine", At(week, 8), At(week, 9), people: new CalendarAssignmentRequest(me.Id, "Organizer"));
        await EventAsync(admin, "Not mine", At(week.AddDays(1), 14), At(week.AddDays(1), 15), priority: "High");
        await EventAsync(admin, "Three-day camp", At(week.AddDays(4), 0), At(week.AddDays(7), 0), allDay: true);
        await EventAsync(admin, "Next week", At(week.AddDays(7), 9), At(week.AddDays(7), 10));

        var all = await BuildAsync("Week", week, new CalendarQuery(default, default));
        Assert.Equal(["Mine", "Not mine", "Three-day camp", "Three-day camp", "Three-day camp"], all.Entries.Select(e => e.Title));
        var camp = all.Entries.Where(e => e.Title == "Three-day camp").ToList();
        Assert.Equal([week.AddDays(4), week.AddDays(5), week.AddDays(6)], camp.Select(e => e.Day));
        Assert.Equal([false, true, true], camp.Select(e => e.ContinuesFromPreviousDay));
        var mine = all.Entries.First();
        Assert.Equal(new TimeOnly(8, 0), mine.Start);
        Assert.Equal(new TimeOnly(9, 0), mine.End);
        Assert.Equal("Meeting", mine.Kind);
        Assert.Equal("Event", mine.Category);
        Assert.Equal("Weekly schedule", all.Title);
        Assert.Equal("20–26 June 2033", all.RangeLabel);
        Assert.Null(all.FilterSummary);

        var onlyMine = await BuildAsync("Week", week, new CalendarQuery(default, default, AssignedUserId: me.Id));
        Assert.Equal(["Mine"], onlyMine.Entries.Select(e => e.Title));
        Assert.Contains("assigned to", onlyMine.FilterSummary);

        var high = await BuildAsync("Week", week, new CalendarQuery(default, default, Priorities: ["High"]));
        Assert.Equal(["Not mine"], high.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task A_year_covers_more_than_the_normal_calendar_range()
    {
        var admin = await factory.AdminClientAsync();
        await EventAsync(admin, "January kickoff", At(new DateOnly(2034, 1, 2), 9), At(new DateOnly(2034, 1, 2), 10));
        await EventAsync(admin, "December carols", At(new DateOnly(2034, 12, 20), 18), At(new DateOnly(2034, 12, 20), 20));

        var year = await BuildAsync("Year", new DateOnly(2034, 7, 1), new CalendarQuery(default, default, SourceTypes: ["CalendarEvent"]));

        Assert.Equal(new DateOnly(2034, 1, 1), year.FirstDay);
        Assert.Equal(new DateOnly(2034, 12, 31), year.LastDay);
        string[] mine = ["January kickoff", "December carols"];
        Assert.Equal(mine, year.Entries.Select(e => e.Title).Where(mine.Contains));
    }

    [Fact]
    public async Task Exports_need_a_signed_in_user_and_a_known_period()
    {
        var admin = await factory.AdminClientAsync();
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/calendar/export?period=Week", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/calendar/export?period=week", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/calendar/export?period=Fortnight", Ct)).StatusCode);
    }

    private Task<ScheduleDocument> BuildAsync(string period, DateOnly date, CalendarQuery filters) =>
        factory.InScopeAsync(sp => sp.GetRequiredService<ScheduleExportService>().BuildAsync(period, date, "Africa/Nairobi", filters, Ct));
}
