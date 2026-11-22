using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class ShowTests(DofApiFactory factory)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");

    // A different far-future year from the other suites keeps calendar assertions independent.
    private static DateOnly Day(int n) => new DateOnly(2034, 3, 6).AddDays(n);

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static async Task<ShowResponse> CreateShowAsync(HttpClient client, string name, Guid? producerId = null,
        string type = "Recurring", int? duration = 90) =>
        await (await client.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest(name, "A weekly conversation", type, producerId, duration), Ct)).DataAsync<ShowResponse>();

    private static async Task<SeasonResponse> CreateSeasonAsync(HttpClient client, Guid showId, int? number = null, string? name = null) =>
        await (await client.PostAsJsonAsync($"/api/v1/shows/{showId}/seasons",
            new CreateSeasonRequest(number, name, Day(0), Day(120)), Ct)).DataAsync<SeasonResponse>();

    private static CreateEpisodeRequest NewEpisode(string title, int? number = null, Guid? producer = null,
        DateTimeOffset? recording = null, DateTimeOffset? recordingEnd = null, string? location = "Studio A",
        DateOnly? editing = null, DateOnly? review = null, DateOnly? approval = null) =>
        new(title, null, number, producer, recording, recordingEnd, location, editing, review, approval);

    private static async Task<EpisodeResponse> CreateEpisodeAsync(HttpClient client, Guid seasonId, CreateEpisodeRequest request) =>
        await (await client.PostAsJsonAsync($"/api/v1/seasons/{seasonId}/episodes", request, Ct)).DataAsync<EpisodeResponse>();

    private static UpdateEpisodeRequest Edit(EpisodeResponse e) => new(e.Title, e.Description, e.Season?.Id, e.EpisodeNumber,
        e.Producer?.Id, e.RecordingStart, e.RecordingEnd, e.RecordingLocation, e.EditingDueDate, e.ReviewDueDate,
        e.ApprovalDueDate, e.Version);

    private static async Task<HttpResponseMessage> SetStatusAsync(HttpClient client, Guid id, string status,
        DateTimeOffset? recordedAt = null, string? note = null) =>
        await client.PatchAsJsonAsync($"/api/v1/episodes/{id}/status", new ChangeEpisodeStatusRequest(status, null, recordedAt, note), Ct);

    private static async Task<List<CalendarItemResponse>> CalendarAsync(HttpClient client, DateOnly from, DateOnly to, string extra = "") =>
        await (await client.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(At(from, 0).ToString("O"))}&end={Uri.EscapeDataString(At(to, 0).ToString("O"))}{extra}", Ct))
            .DataAsync<List<CalendarItemResponse>>();

    // ---------- shows and seasons ----------

    [Fact]
    public async Task Shows_need_episodes_manage_and_a_show_can_have_multiple_seasons()
    {
        var admin = await factory.AdminClientAsync();
        var (producer, producerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest("Nope", null, "Recurring", null, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest("", null, "Weekly", null, 0), Ct)).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest("Faith Conversations", "Weekly talk show", "Recurring", producer.Id, 60), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var show = await created.DataAsync<ShowResponse>();
        Assert.Equal("InDevelopment", show.Status);
        Assert.Equal(producer.Id, show.Producer!.Id);

        // The show's producer edits it and adds seasons without holding episodes.manage.
        var s1 = await CreateSeasonAsync(producerClient, show.Id, name: "Beginnings");
        var s2 = await CreateSeasonAsync(producerClient, show.Id);
        Assert.Equal((1, 2), (s1.SeasonNumber, s2.SeasonNumber));
        Assert.Equal("Season 1: Beginnings", s1.DisplayName);
        Assert.Equal(HttpStatusCode.Conflict, (await producerClient.PostAsJsonAsync($"/api/v1/shows/{show.Id}/seasons",
            new CreateSeasonRequest(2, null, null, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/v1/shows/{show.Id}/seasons",
            new CreateSeasonRequest(null, null, null, null), Ct)).StatusCode);

        var updated = await (await producerClient.PutAsJsonAsync($"/api/v1/seasons/{s2.Id}",
            new UpdateSeasonRequest(3, "Growth", Day(130), Day(200), "InProduction", s2.Version), Ct)).DataAsync<SeasonResponse>();
        Assert.Equal((3, "InProduction"), (updated.SeasonNumber, updated.Status));

        var detail = await (await member.GetAsync($"/api/v1/shows/{show.Id}", Ct)).DataAsync<ShowResponse>();
        Assert.Equal([1, 3], detail.Seasons.Select(s => s.SeasonNumber));
        Assert.False(detail.CanEdit);

        // Only episodes.manage archives; an archived show takes no new seasons or episodes.
        Assert.Equal(HttpStatusCode.Forbidden, (await producerClient.PatchAsJsonAsync($"/api/v1/shows/{show.Id}/status",
            new ChangeStatusRequest("Archived"), Ct)).StatusCode);
        var archived = await (await admin.PatchAsJsonAsync($"/api/v1/shows/{show.Id}/status",
            new ChangeStatusRequest("Archived"), Ct)).DataAsync<ShowResponse>();
        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await producerClient.PostAsJsonAsync($"/api/v1/seasons/{s1.Id}/episodes",
            NewEpisode("Too late"), Ct)).StatusCode);

        var list = await (await admin.GetAsync("/api/v1/shows?pageSize=100", Ct)).DataAsync<PagedResponse<ShowSummaryResponse>>();
        Assert.DoesNotContain(list.Items, s => s.Id == show.Id);
        list = await (await admin.GetAsync("/api/v1/shows?pageSize=100&includeArchived=true", Ct)).DataAsync<PagedResponse<ShowSummaryResponse>>();
        Assert.Equal(2, list.Items.Single(s => s.Id == show.Id).SeasonCount);

        var history = await (await admin.GetAsync($"/api/v1/shows/{show.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary == "Status changed from InDevelopment to Archived");
        Assert.Contains(history, h => h.EntityType == "Season" && h.Summary == "Status changed from Planned to InProduction");
    }

    // ---------- episodes ----------

    [Fact]
    public async Task Seasons_have_multiple_numbered_episodes_and_one_offs_need_no_season()
    {
        var admin = await factory.AdminClientAsync();
        var show = await CreateShowAsync(admin, "Sunday Service Live");
        var season = await CreateSeasonAsync(admin, show.Id);
        var other = await CreateSeasonAsync(await factory.AdminClientAsync(), (await CreateShowAsync(admin, "Other show")).Id);

        var e1 = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Grace"));
        var e2 = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Hope"));
        var e5 = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Love", number: 5));
        Assert.Equal((1, 2, 5), (e1.EpisodeNumber, e2.EpisodeNumber, e5.EpisodeNumber));
        Assert.Equal("S1 E2", e2.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes",
            NewEpisode("Duplicate", number: 2), Ct)).StatusCode);

        var episodes = await (await admin.GetAsync($"/api/v1/seasons/{season.Id}/episodes", Ct)).DataAsync<List<EpisodeSummaryResponse>>();
        Assert.Equal(["Grace", "Hope", "Love"], episodes.Select(e => e.Title));

        // One-off productions live directly under the show.
        var special = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/episodes",
            NewEpisode("Christmas special"), Ct)).DataAsync<EpisodeResponse>();
        Assert.Null(special.Season);
        Assert.Equal("", special.Code);
        var all = await (await admin.GetAsync($"/api/v1/shows/{show.Id}/episodes", Ct)).DataAsync<List<EpisodeSummaryResponse>>();
        Assert.Equal("Christmas special", all.Last().Title);

        // Moving to a season of another show is rejected; moving out of the season is fine.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"/api/v1/episodes/{e5.Id}",
            Edit(e5) with { SeasonId = other.Id }, Ct)).StatusCode);
        var moved = await (await admin.PutAsJsonAsync($"/api/v1/episodes/{e5.Id}",
            Edit(e5) with { SeasonId = null, EpisodeNumber = null }, Ct)).DataAsync<EpisodeResponse>();
        Assert.Null(moved.Season);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/episodes/{e5.Id}",
            Edit(e5) with { Title = "Stale" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes",
            NewEpisode("Backwards", recording: At(Day(3), 10), recordingEnd: At(Day(3), 9)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes",
            NewEpisode("Out of order", editing: Day(10), review: Day(5)), Ct)).StatusCode);
    }

    [Fact]
    public async Task Episodes_can_have_different_producers_and_editors()
    {
        var admin = await factory.AdminClientAsync();
        var (producerA, producerAClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (producerB, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (editorA, editorAClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (editorB, _) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var show = await CreateShowAsync(admin, "Youth Hour");
        var season = await CreateSeasonAsync(admin, show.Id);

        var first = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Identity", producer: producerA.Id));
        var second = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Purpose", producer: producerB.Id));
        Assert.Equal("Producer", Assert.Single(first.Assignments).Responsibility);

        // The episode's own producer assigns the crew; a producer of another episode cannot.
        first = await (await producerAClient.PostAsJsonAsync($"/api/v1/episodes/{first.Id}/assignments",
            new AssignEpisodeRequest(editorA.Id, "Editor"), Ct)).DataAsync<EpisodeResponse>();
        first = await (await producerAClient.PostAsJsonAsync($"/api/v1/episodes/{first.Id}/assignments",
            new AssignEpisodeRequest(editorA.Id, "Presenter"), Ct)).DataAsync<EpisodeResponse>();
        Assert.Equal(HttpStatusCode.Forbidden, (await producerAClient.PostAsJsonAsync($"/api/v1/episodes/{second.Id}/assignments",
            new AssignEpisodeRequest(editorA.Id, "Editor"), Ct)).StatusCode);
        second = await (await admin.PostAsJsonAsync($"/api/v1/episodes/{second.Id}/assignments",
            new AssignEpisodeRequest(editorB.Id, "Editor"), Ct)).DataAsync<EpisodeResponse>();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/episodes/{second.Id}/assignments",
            new AssignEpisodeRequest(editorB.Id, "Caterer"), Ct)).StatusCode);

        Assert.Equal(producerA.Id, first.Producer!.Id);
        Assert.Equal(producerB.Id, second.Producer!.Id);
        Assert.Contains(first.Assignments, a => a.UserId == editorA.Id && a.Responsibility == "Editor");
        Assert.Contains(first.Assignments, a => a.UserId == editorA.Id && a.Responsibility == "Presenter");
        Assert.Contains(second.Assignments, a => a.UserId == editorB.Id && a.Responsibility == "Editor");
        Assert.DoesNotContain(second.Assignments, a => a.UserId == editorA.Id);

        // Crew can move the episode along but cannot change its details.
        var editorView = await (await editorAClient.GetAsync($"/api/v1/episodes/{first.Id}", Ct)).DataAsync<EpisodeResponse>();
        Assert.False(editorView.CanEdit);
        Assert.True(editorView.CanChangeStatus);
        Assert.Equal(HttpStatusCode.Forbidden, (await editorAClient.PutAsJsonAsync($"/api/v1/episodes/{first.Id}",
            Edit(editorView) with { Title = "Mine now" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(editorAClient, first.Id, "Editing")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatusAsync(editorAClient, second.Id, "Editing")).StatusCode);

        // The producer's own assignment stays while they are the producer.
        var producerAssignment = first.Assignments.Single(a => a.Responsibility == "Producer");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.DeleteAsync($"/api/v1/episodes/{first.Id}/assignments/{producerAssignment.Id}", Ct)).StatusCode);
        var presenter = first.Assignments.Single(a => a.Responsibility == "Presenter");
        var after = await (await admin.DeleteAsync($"/api/v1/episodes/{first.Id}/assignments/{presenter.Id}", Ct)).DataAsync<EpisodeResponse>();
        Assert.DoesNotContain(after.Assignments, a => a.Id == presenter.Id);

        var mine = await (await editorAClient.GetAsync("/api/v1/episodes?mine=true", Ct)).DataAsync<PagedResponse<EpisodeSummaryResponse>>();
        Assert.Equal(first.Id, Assert.Single(mine.Items).Id);
        var personal = await (await editorAClient.GetAsync("/api/v1/dashboard/personal", Ct)).DataAsync<PersonalDashboardResponse>();
        Assert.Contains(personal.MyEpisodes, e => e.Id == first.Id);

        var history = await (await admin.GetAsync($"/api/v1/episodes/{first.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary == "Test User assigned as Editor");
        Assert.Contains(history, h => h.Summary == "Test User removed as Presenter");
    }

    [Fact]
    public async Task Recording_dates_and_deadlines_appear_in_the_calendar_from_the_episode()
    {
        var admin = await factory.AdminClientAsync();
        var (editor, editorClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var show = await CreateShowAsync(admin, "Morning Devotion", duration: 45);
        var season = await CreateSeasonAsync(admin, show.Id);
        var episode = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Psalm 23", recording: At(Day(10), 9),
            editing: Day(12), review: Day(13), approval: Day(14)));
        await admin.PostAsJsonAsync($"/api/v1/episodes/{episode.Id}/assignments", new AssignEpisodeRequest(editor.Id, "Editor"), Ct);
        Assert.Equal(At(Day(10), 9).AddMinutes(45), episode.RecordingEnd);

        var items = await CalendarAsync(admin, Day(9), Day(16), $"&showId={show.Id}");
        Assert.Equal(["Recording", "EditingDeadline", "ReviewDeadline", "ApprovalDeadline"], items.Select(i => i.EventType));
        Assert.All(items, i => Assert.Equal((episode.Id, "Episode"), (i.SourceId, i.SourceType)));
        var recording = items[0];
        Assert.Equal(("Studio A", false, At(Day(10), 9)), (recording.Location, recording.IsAllDay, recording.StartAt));
        Assert.Equal("Recording · Morning Devotion S1 E1: Psalm 23", recording.Title);
        Assert.True(items[1].IsAllDay);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());

        // The editor sees the recording and the editing deadline, not review or approval.
        var editorItems = await CalendarAsync(editorClient, Day(9), Day(16), $"&showId={show.Id}&assignedUserId={editor.Id}");
        Assert.Equal(["Recording", "EditingDeadline"], editorItems.Select(i => i.EventType));
        Assert.False(editorItems[0].CanEdit);

        // Rescheduling edits the episode: the item moves and is not duplicated.
        episode = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}", Ct)).DataAsync<EpisodeResponse>();
        await (await admin.PutAsJsonAsync($"/api/v1/episodes/{episode.Id}", Edit(episode) with
        {
            RecordingStart = At(Day(11), 14), RecordingEnd = At(Day(11), 16),
        }, Ct)).DataAsync<EpisodeResponse>();
        items = await CalendarAsync(admin, Day(9), Day(16), $"&showId={show.Id}&eventTypes=Recording");
        Assert.Equal(At(Day(11), 14), Assert.Single(items).StartAt);

        // Deadlines the episode has passed through drop off; cancelled episodes leave the calendar.
        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, episode.Id, "InReview")).StatusCode);
        items = await CalendarAsync(admin, Day(9), Day(16), $"&showId={show.Id}");
        Assert.Equal(["Recording", "ReviewDeadline", "ApprovalDeadline"], items.Select(i => i.EventType));
        await SetStatusAsync(admin, episode.Id, "Cancelled");
        Assert.Empty(await CalendarAsync(admin, Day(9), Day(16), $"&showId={show.Id}"));
        Assert.NotEmpty(await CalendarAsync(admin, Day(9), Day(16), $"&showId={show.Id}&includeCancelled=true"));

        // Episodes have no project, so a project filter excludes them.
        Assert.DoesNotContain(await CalendarAsync(admin, Day(9), Day(16), $"&projectId={Guid.NewGuid()}&includeCancelled=true"),
            i => i.SourceType == "Episode");
    }

    [Fact]
    public async Task The_lifecycle_allows_corrections_keeps_history_and_leaves_publication_to_releases()
    {
        var admin = await factory.AdminClientAsync();
        var show = await CreateShowAsync(admin, "Bible Study Hour");
        var season = await CreateSeasonAsync(admin, show.Id);
        var episode = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Romans 8", location: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetStatusAsync(admin, episode.Id, "Scheduled")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetStatusAsync(admin, episode.Id, "Published")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetStatusAsync(admin, episode.Id, "Aired")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SetStatusAsync(admin, episode.Id, "Recorded", DateTimeOffset.UtcNow.AddDays(1))).StatusCode);

        // Stages can be skipped: straight from Planned to Editing records when it was recorded.
        var recordedAt = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        var editing = await (await SetStatusAsync(admin, episode.Id, "Editing", recordedAt)).DataAsync<EpisodeResponse>();
        Assert.Equal(recordedAt, editing.RecordedAt);
        Assert.Equal(["Done", "Done", "Current", "Upcoming", "Upcoming", "Upcoming", "Upcoming"], editing.Timeline.Select(s => s.State));

        // A correction back to Planned clears the recording time but keeps the history.
        var corrected = await (await SetStatusAsync(admin, episode.Id, "Planned", note: "Recording failed, re-shoot"))
            .DataAsync<EpisodeResponse>();
        Assert.Null(corrected.RecordedAt);
        var history = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary == "Status changed from Planned to Editing");
        Assert.Contains(history, h => h.Summary == "Status changed from Editing to Planned: Recording failed, re-shoot");

        var stale = await admin.PatchAsJsonAsync($"/api/v1/episodes/{episode.Id}/status",
            new ChangeEpisodeStatusRequest("Approved", episode.Version), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var approved = await (await SetStatusAsync(admin, episode.Id, "Approved")).DataAsync<EpisodeResponse>();
        Assert.NotNull(approved.RecordedAt);
        var management = await (await admin.GetAsync("/api/v1/dashboard/management", Ct)).DataAsync<ManagementDashboardResponse>();
        Assert.Contains(management.AwaitingPublication, e => e.Id == episode.Id);

        var cancelled = await (await SetStatusAsync(admin, episode.Id, "Cancelled")).DataAsync<EpisodeResponse>();
        Assert.NotNull(cancelled.ArchivedAt);
        Assert.Contains(cancelled.Timeline, s => s.State == "Cancelled");
    }

    [Fact]
    public async Task Missed_deadlines_make_episodes_overdue()
    {
        var admin = await factory.AdminClientAsync();
        var (producer, producerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var show = await CreateShowAsync(admin, "Late Night Worship");
        var season = await CreateSeasonAsync(admin, show.Id);
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var late = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Behind", producer: producer.Id, editing: past));
        var onTime = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Ahead", producer: producer.Id, editing: past.AddDays(30)));

        Assert.True(late.IsOverdue);
        Assert.False(onTime.IsOverdue);
        var overdue = await (await producerClient.GetAsync("/api/v1/dashboard/overdue?mine=true", Ct)).DataAsync<OverdueResponse>();
        Assert.Equal(late.Id, Assert.Single(overdue.Episodes).Id);

        // Reaching review meets the editing deadline.
        await SetStatusAsync(producerClient, late.Id, "InReview");
        overdue = await (await producerClient.GetAsync("/api/v1/dashboard/overdue?mine=true", Ct)).DataAsync<OverdueResponse>();
        Assert.Empty(overdue.Episodes);
    }

    [Fact]
    public async Task Tasks_can_belong_to_an_episode_but_not_also_to_a_project()
    {
        var admin = await factory.AdminClientAsync();
        var show = await CreateShowAsync(admin, "Testimony Tuesday");
        var season = await CreateSeasonAsync(admin, show.Id);
        var episode = await CreateEpisodeAsync(admin, season.Id, NewEpisode("Restored"));
        var project = await (await admin.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Unrelated", null, "Other", "Normal", null, null, null), Ct)).DataAsync<ProjectResponse>();

        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest("Colour grade", null, null,
            At(Day(20), 12), "High", null, null, episode.Id), Ct)).DataAsync<TaskResponse>();
        Assert.Equal((episode.Id, "Testimony Tuesday", "S1 E1"), (task.Episode!.Id, task.Episode.ShowName, task.Episode.Code));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest("Both", null,
            project.Id, null, "Normal", null, null, episode.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest("Ghost", null,
            null, null, "Normal", null, null, Guid.NewGuid()), Ct)).StatusCode);

        var detail = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}", Ct)).DataAsync<EpisodeResponse>();
        Assert.Equal(task.Id, Assert.Single(detail.Tasks).Id);
        var byEpisode = await (await admin.GetAsync($"/api/v1/tasks?episodeId={episode.Id}", Ct)).DataAsync<PagedResponse<TaskResponse>>();
        Assert.Equal(task.Id, Assert.Single(byEpisode.Items).Id);

        // The show filter on the calendar now includes the show's episode tasks.
        var items = await CalendarAsync(admin, Day(19), Day(22), $"&showId={show.Id}&eventTypes=TaskDeadline");
        Assert.Equal("Testimony Tuesday: Restored", Assert.Single(items).Location);

        // Cancelled episodes take no new tasks.
        await SetStatusAsync(admin, episode.Id, "Cancelled");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest("Late",
            null, null, null, "Normal", null, null, episode.Id), Ct)).StatusCode);
    }
}
