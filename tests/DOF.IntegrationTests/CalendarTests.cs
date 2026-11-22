using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class CalendarTests(DofApiFactory factory)
{
    // Each test works in its own far-future week so tests sharing the database never see each other's events.
    private static DateTimeOffset Week(int n) => new DateTimeOffset(2031, 1, 6, 0, 0, 0, TimeSpan.Zero).AddDays(7 * n);

    private static CreateCalendarEventRequest Event(string title, DateTimeOffset start, DateTimeOffset end,
        string type = "Meeting", string priority = "Normal", bool allDay = false, params CalendarAssignmentRequest[] assignees) =>
        new(title, "Agenda", type, start, end, allDay, "Studio A", null, priority, assignees);

    private static async Task<CalendarEventResponse> CreateAsync(HttpClient client, CreateCalendarEventRequest request) =>
        await (await client.PostAsJsonAsync("/api/v1/calendar/events", request, Ct)).DataAsync<CalendarEventResponse>();

    private static async Task<List<CalendarItemResponse>> CalendarAsync(HttpClient client, DateTimeOffset start, DateTimeOffset end, string extra = "") =>
        await (await client.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(end.ToString("O"))}{extra}", Ct))
            .DataAsync<List<CalendarItemResponse>>();

    [Fact]
    public async Task Events_are_created_with_their_assignments_and_creator()
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        var crew = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var start = Week(0).AddHours(7);

        var response = await admin.PostAsJsonAsync("/api/v1/calendar/events", Event("Staff meeting", start, start.AddHours(1),
            assignees: [new(me.Id, "Organizer"), new(crew.Id, "Attendee")]), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.DataAsync<CalendarEventResponse>();
        Assert.Equal("Staff meeting", created.Title);
        Assert.Equal(start, created.StartAt);
        Assert.Equal("Scheduled", created.Status);
        Assert.Equal(me.Id, created.CreatedBy);
        Assert.Equal(me.FullName, created.CreatedByName);
        Assert.True(created.CanEdit);
        Assert.Equal(["Organizer", "Attendee"], created.Assignments.Select(a => a.AssignmentType));
        Assert.Equal(response.Headers.Location?.AbsolutePath, $"/api/v1/calendar/events/{created.Id}");
    }

    [Fact]
    public async Task Team_members_can_read_the_calendar_but_not_manage_events()
    {
        var admin = await factory.AdminClientAsync();
        var start = Week(1).AddHours(9);
        await CreateAsync(admin, Event("Visible to everyone", start, start.AddHours(1)));
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var client = factory.CreateClient().WithToken((await factory.LoginAsync(member.Email, member.Password)).AccessToken);

        var items = await CalendarAsync(client, Week(1), Week(2));
        Assert.False(Assert.Single(items).CanEdit);

        var create = await client.PostAsJsonAsync("/api/v1/calendar/events", Event("Nope", start, start.AddHours(1)), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Invalid_events_are_rejected_with_field_errors()
    {
        var admin = await factory.AdminClientAsync();
        var start = Week(2);

        var response = await admin.PostAsJsonAsync("/api/v1/calendar/events",
            new CreateCalendarEventRequest("", null, "Party", start, start.AddHours(-1), false, null, "not-a-link", "Extreme", null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        foreach (var field in new[] { "Title", "EventType", "EndAt", "MeetingUrl", "Priority" })
        {
            Assert.Contains($"\"{field}\"", body);
        }
    }

    [Fact]
    public async Task Events_can_only_be_assigned_to_active_users()
    {
        var admin = await factory.AdminClientAsync();
        var start = Week(3);

        var response = await admin.PostAsJsonAsync("/api/v1/calendar/events",
            Event("Ghost crew", start, start.AddHours(1), assignees: [new(Guid.NewGuid(), "Attendee")]), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task The_calendar_returns_events_overlapping_the_range_in_time_order()
    {
        var admin = await factory.AdminClientAsync();
        var from = Week(4);
        var to = from.AddDays(1);
        await CreateAsync(admin, Event("Spans start", from.AddHours(-2), from.AddHours(1)));
        await CreateAsync(admin, Event("Inside", from.AddHours(10), from.AddHours(11)));
        await CreateAsync(admin, Event("Spans end", to.AddHours(-1), to.AddHours(2)));
        await CreateAsync(admin, Event("Ends exactly at start", from.AddHours(-1), from));
        await CreateAsync(admin, Event("Starts exactly at end", to, to.AddHours(1)));

        var items = await CalendarAsync(admin, from, to);

        Assert.Equal(["Spans start", "Inside", "Spans end"], items.Select(i => i.Title));
        Assert.All(items, i => Assert.Equal("CalendarEvent", i.SourceType));
        Assert.All(items, i => Assert.Equal($"CalendarEvent:{i.SourceId}", i.Id));
    }

    [Fact]
    public async Task Calendar_filters_narrow_the_results()
    {
        var admin = await factory.AdminClientAsync();
        var crew = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var day = Week(5);
        await CreateAsync(admin, Event("Training", day.AddHours(8), day.AddHours(9), type: "Training", priority: "High",
            assignees: new CalendarAssignmentRequest(crew.Id, "Attendee")));
        await CreateAsync(admin, Event("Meeting", day.AddHours(10), day.AddHours(11)));

        async Task<string[]> Titles(string filter) =>
            [.. (await CalendarAsync(admin, day, day.AddDays(1), filter)).Select(i => i.Title)];

        Assert.Equal(["Training"], await Titles("&eventTypes=Training"));
        Assert.Equal(["Training", "Meeting"], await Titles("&eventTypes=Training&eventTypes=Meeting"));
        Assert.Equal(["Training"], await Titles("&priorities=High"));
        Assert.Equal(["Training"], await Titles($"&assignedUserId={crew.Id}"));
        Assert.Equal(["Training", "Meeting"], await Titles("&sourceTypes=CalendarEvent"));
        Assert.Empty(await Titles("&sourceTypes=Release"));
        Assert.Empty(await Titles($"&projectId={Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Rescheduling_moves_the_single_source_record_without_duplicating_it()
    {
        var admin = await factory.AdminClientAsync();
        var day = Week(6);
        var created = await CreateAsync(admin, Event("Recording", day.AddHours(7), day.AddHours(10), type: "RecordingSession"));
        var producer = await factory.CreateUserAsync(SystemRoles.Producer);
        var producerClient = factory.CreateClient().WithToken((await factory.LoginAsync(producer.Email, producer.Password)).AccessToken);

        var moved = await (await producerClient.PutAsJsonAsync($"/api/v1/calendar/events/{created.Id}", new UpdateCalendarEventRequest(
                created.Title, created.Description, created.EventType, day.AddDays(2).AddHours(8), day.AddDays(2).AddHours(11),
                false, created.Location, null, created.Priority, created.Status, [], created.Version), Ct))
            .DataAsync<CalendarEventResponse>();

        Assert.Equal(created.Version + 1, moved.Version);
        Assert.Equal(created.CreatedBy, moved.CreatedBy);
        var item = Assert.Single(await CalendarAsync(admin, day, day.AddDays(7)));
        Assert.Equal(created.Id, item.SourceId);
        Assert.Equal(day.AddDays(2).AddHours(8), item.StartAt);

        var stale = await admin.PutAsJsonAsync($"/api/v1/calendar/events/{created.Id}", new UpdateCalendarEventRequest(
            created.Title, null, created.EventType, day.AddHours(1), day.AddHours(2), false, null, null, created.Priority,
            created.Status, [], created.Version), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Cancelled_events_leave_the_calendar_but_are_kept()
    {
        var admin = await factory.AdminClientAsync();
        var day = Week(7);
        var created = await CreateAsync(admin, Event("Called off", day.AddHours(9), day.AddHours(10)));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/calendar/events/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/calendar/events/{created.Id}", Ct)).StatusCode);

        Assert.Empty(await CalendarAsync(admin, day, day.AddDays(1)));
        Assert.Equal("Cancelled", Assert.Single(await CalendarAsync(admin, day, day.AddDays(1), "&includeCancelled=true")).Status);
        Assert.Single(await CalendarAsync(admin, day, day.AddDays(1), "&statuses=Cancelled"));
        var kept = await (await admin.GetAsync($"/api/v1/calendar/events/{created.Id}", Ct)).DataAsync<CalendarEventResponse>();
        Assert.Equal("Cancelled", kept.Status);
    }

    [Fact]
    public async Task Day_week_month_and_agenda_views_use_the_users_timezone()
    {
        var admin = await factory.AdminClientAsync();
        // 22:30 UTC on Wednesday 9 April 2031 is 01:30 on Thursday 10 April in Nairobi (UTC+3).
        var late = new DateTimeOffset(2031, 4, 9, 22, 30, 0, TimeSpan.Zero);
        await CreateAsync(admin, Event("Late-night edit", late, late.AddMinutes(30)));

        async Task<CalendarRangeResponse> View(string path) =>
            await (await admin.GetAsync(path, Ct)).DataAsync<CalendarRangeResponse>();

        var thursday = await View("/api/v1/calendar/day?date=2031-04-10");
        Assert.Equal("Africa/Nairobi", thursday.TimeZone);
        Assert.Equal(new DateTimeOffset(2031, 4, 9, 21, 0, 0, TimeSpan.Zero), thursday.Start);
        Assert.Single(thursday.Items);
        Assert.Empty((await View("/api/v1/calendar/day?date=2031-04-09")).Items);
        Assert.Single((await View("/api/v1/calendar/day?date=2031-04-09&timeZone=UTC")).Items);

        var week = await View("/api/v1/calendar/week?date=2031-04-10");
        Assert.Equal(new DateTimeOffset(2031, 4, 6, 21, 0, 0, TimeSpan.Zero), week.Start); // Monday 7 April, 00:00 Nairobi
        Assert.Equal(week.Start.AddDays(7), week.End);
        Assert.Single(week.Items);

        var month = await View("/api/v1/calendar/month?year=2031&month=4");
        Assert.Equal(new DateTimeOffset(2031, 3, 31, 21, 0, 0, TimeSpan.Zero), month.Start);
        Assert.Equal(new DateTimeOffset(2031, 4, 30, 21, 0, 0, TimeSpan.Zero), month.End);
        Assert.Contains(month.Items, i => i.Title == "Late-night edit");

        var agenda = await View("/api/v1/calendar/agenda?start=2031-04-08&days=3");
        Assert.Equal(TimeSpan.FromDays(3), agenda.End - agenda.Start);
        Assert.Contains(agenda.Items, i => i.Title == "Late-night edit");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/calendar/agenda?days=365", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/calendar/day?timeZone=Mars/Base", Ct)).StatusCode);
    }

    [Fact]
    public async Task All_day_events_cover_whole_days_in_the_organisation_timezone()
    {
        var admin = await factory.AdminClientAsync();
        var created = await CreateAsync(admin, Event("Conference", new DateTimeOffset(2031, 6, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2031, 6, 11, 15, 0, 0, TimeSpan.Zero), type: "MinistryEvent", allDay: true));

        Assert.Equal(new DateTimeOffset(2031, 6, 9, 21, 0, 0, TimeSpan.Zero), created.StartAt);  // 10 June 00:00 Nairobi
        Assert.Equal(new DateTimeOffset(2031, 6, 11, 21, 0, 0, TimeSpan.Zero), created.EndAt);   // 12 June 00:00 Nairobi

        // Saving it again unchanged must not lengthen it.
        var saved = await (await admin.PutAsJsonAsync($"/api/v1/calendar/events/{created.Id}", new UpdateCalendarEventRequest(
                created.Title, created.Description, created.EventType, created.StartAt, created.EndAt, true, created.Location,
                null, created.Priority, created.Status, [], created.Version), Ct))
            .DataAsync<CalendarEventResponse>();
        Assert.Equal(created.StartAt, saved.StartAt);
        Assert.Equal(created.EndAt, saved.EndAt);
    }

    [Fact]
    public async Task Calendar_ranges_are_validated()
    {
        var admin = await factory.AdminClientAsync();
        var start = Week(8);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.GetAsync($"/api/v1/calendar?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(start.AddDays(-1).ToString("O"))}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.GetAsync($"/api/v1/calendar?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(start.AddDays(400).ToString("O"))}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Dashboards_show_my_schedule_and_unassigned_activities()
    {
        var admin = await factory.AdminClientAsync();
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var memberClient = factory.CreateClient().WithToken((await factory.LoginAsync(member.Email, member.Password)).AccessToken);
        var now = DateTimeOffset.UtcNow;
        var marker = Guid.NewGuid().ToString("N")[..6];

        await CreateAsync(admin, Event($"Now {marker}", now.AddSeconds(-1), now.AddSeconds(1),
            assignees: new CalendarAssignmentRequest(member.Id, "Attendee")));
        await CreateAsync(admin, Event($"Soon {marker}", now.AddDays(3), now.AddDays(3).AddHours(1),
            assignees: new CalendarAssignmentRequest(member.Id, "Attendee")));
        await CreateAsync(admin, Event($"Nobody {marker}", now.AddDays(2), now.AddDays(2).AddHours(1)));

        var personal = await (await memberClient.GetAsync("/api/v1/dashboard/personal", Ct)).DataAsync<PersonalDashboardResponse>();
        Assert.Equal([$"Now {marker}"], personal.TodaySchedule.Select(i => i.Title));
        Assert.Equal([$"Soon {marker}"], personal.Upcoming.Select(i => i.Title));

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/v1/dashboard/management", Ct)).StatusCode);
        var management = await (await admin.GetAsync("/api/v1/dashboard/management", Ct)).DataAsync<ManagementDashboardResponse>();
        Assert.Contains(management.UpcomingActivities, i => i.Title == $"Soon {marker}");
        Assert.Contains(management.Unassigned, i => i.Title == $"Nobody {marker}");
        Assert.DoesNotContain(management.Unassigned, i => i.Title == $"Soon {marker}");

        var mine = await (await memberClient.GetAsync("/api/v1/dashboard/upcoming?days=7&mine=true", Ct)).DataAsync<CalendarRangeResponse>();
        Assert.Equal([$"Now {marker}", $"Soon {marker}"], mine.Items.Where(i => i.Title.EndsWith(marker)).Select(i => i.Title));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await memberClient.GetAsync("/api/v1/dashboard/upcoming?days=0", Ct)).StatusCode);
    }

    [Fact]
    public void Shared_calendar_values_match_the_domain() =>
        Assert.Equal(Enum.GetNames<DOF.Domain.Enums.CalendarEventType>(), CalendarValues.EventTypes);
}
