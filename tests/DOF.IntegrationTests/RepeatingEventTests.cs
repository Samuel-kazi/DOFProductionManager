using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

/// <summary>Repeating events such as a weekly staff meeting: one occurrence per date, edited and cancelled as a series.</summary>
public class RepeatingEventTests(DofApiFactory factory)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private async Task<List<CalendarItemResponse>> ItemsAsync(HttpClient client, DateOnly from, DateOnly to, string title) =>
        [.. (await (await client.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(At(from, 0).ToString("O"))}&end={Uri.EscapeDataString(At(to, 0).ToString("O"))}&sourceTypes=CalendarEvent", Ct))
            .DataAsync<List<CalendarItemResponse>>()).Where(i => i.Title.StartsWith(title)).OrderBy(i => i.StartAt)];

    [Fact]
    public async Task A_weekly_meeting_is_created_edited_from_one_date_on_and_stopped()
    {
        var admin = await factory.AdminClientAsync();
        var tuesday = new DateOnly(2036, 3, 4);
        var title = $"Staff meeting {Guid.NewGuid():N}"[..21];
        var created = await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(title, null, "Meeting",
            At(tuesday, 10), At(tuesday, 11), false, "Board room", null, "Normal", [],
            new RepeatRequest("Weekly", 1, ["Tuesday", "Thursday"], tuesday.AddDays(14))), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.DataAsync<CalendarEventResponse>();
        Assert.Equal("Every week on Tue and Thu until 18 Mar 2036", first.RepeatText);

        var items = await ItemsAsync(admin, tuesday, tuesday.AddDays(21), title);
        Assert.Equal([4, 6, 11, 13, 18], items.Select(i => TimeZoneInfo.ConvertTime(i.StartAt, Nairobi).Day));
        Assert.All(items, i => Assert.True(i.IsRepeating));
        Assert.All(items, i => Assert.Equal(10, TimeZoneInfo.ConvertTime(i.StartAt, Nairobi).Hour));

        // From 11 March on, the meeting moves to 14:00 in another room; earlier ones stay.
        var eleventh = await (await admin.GetAsync($"/api/v1/calendar/events/{items[2].SourceId}", Ct)).DataAsync<CalendarEventResponse>();
        var moved = await admin.PutAsJsonAsync($"/api/v1/calendar/events/{eleventh.Id}", new UpdateCalendarEventRequest(
            title, null, "Meeting", At(tuesday.AddDays(7), 14), At(tuesday.AddDays(7), 15), false, "Studio B", null, "Normal", "Scheduled", [],
            eleventh.Version, ApplyToFollowing: true), Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        items = await ItemsAsync(admin, tuesday, tuesday.AddDays(21), title);
        Assert.Equal([10, 10, 14, 14, 14], items.Select(i => TimeZoneInfo.ConvertTime(i.StartAt, Nairobi).Hour));
        Assert.Equal(["Board room", "Board room", "Studio B", "Studio B", "Studio B"], items.Select(i => i.Location));

        // Stopping the series from 13 March cancels that one and the 18th.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/calendar/events/{items[3].SourceId}?following=true", Ct)).StatusCode);
        Assert.Equal(3, (await ItemsAsync(admin, tuesday, tuesday.AddDays(21), title)).Count);
    }

    [Fact]
    public async Task A_repeat_needs_a_valid_rule()
    {
        var admin = await factory.AdminClientAsync();
        var day = new DateOnly(2036, 5, 6);
        async Task<HttpStatusCode> CreateAsync(RepeatRequest repeat) => (await admin.PostAsJsonAsync("/api/v1/calendar/events",
            new CreateCalendarEventRequest("Bad repeat", null, "Meeting", At(day, 9), At(day, 10), false, null, null, "Normal", [], repeat), Ct)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await CreateAsync(new RepeatRequest("Hourly", 1, null, day.AddDays(5))));
        Assert.Equal(HttpStatusCode.BadRequest, await CreateAsync(new RepeatRequest("Daily")));                        // no end
        Assert.Equal(HttpStatusCode.BadRequest, await CreateAsync(new RepeatRequest("Weekly", 1, ["Funday"], day.AddDays(5))));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await CreateAsync(new RepeatRequest("Daily", 1, null, day.AddYears(3))));
    }
}
