using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class ReleaseTests(DofApiFactory factory)
{
    private static readonly TimeZoneInfo Nairobi = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");

    // A far-future window of its own keeps calendar assertions independent of other suites.
    private static DateOnly Day(int n) => new DateOnly(2034, 9, 4).AddDays(n);

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Nairobi), TimeSpan.Zero);

    private static async Task<Dictionary<string, Guid>> ChannelsAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/distribution-channels", Ct)).DataAsync<List<DistributionChannelResponse>>())
        .ToDictionary(c => c.Name, c => c.Id);

    private static async Task<(ShowResponse Show, EpisodeResponse Episode)> EpisodeAsync(HttpClient admin, string name)
    {
        var show = await (await admin.PostAsJsonAsync("/api/v1/shows",
            new CreateShowRequest(name, null, "Recurring", null, 60), Ct)).DataAsync<ShowResponse>();
        var season = await (await admin.PostAsJsonAsync($"/api/v1/shows/{show.Id}/seasons",
            new CreateSeasonRequest(null, null, null, null), Ct)).DataAsync<SeasonResponse>();
        var episode = await (await admin.PostAsJsonAsync($"/api/v1/seasons/{season.Id}/episodes", new CreateEpisodeRequest(
            "Grace", null, null, null, null, null, null, null, null, null), Ct)).DataAsync<EpisodeResponse>();
        await admin.PatchAsJsonAsync($"/api/v1/episodes/{episode.Id}/status", new ChangeEpisodeStatusRequest("ReadyForRelease"), Ct);
        return (show, episode);
    }

    private static async Task<ReleaseResponse> ScheduleAsync(HttpClient client, Guid episodeId, Guid channelId, DateTimeOffset? at,
        Guid? assignee = null, string status = "Scheduled") =>
        await (await client.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episodeId, channelId, at, status, assignee), Ct)).DataAsync<ReleaseResponse>();

    private static async Task<HttpResponseMessage> PublishAsync(HttpClient client, Guid id, DateTimeOffset at, bool confirmed = true,
        string? url = null) =>
        await client.PatchAsJsonAsync($"/api/v1/releases/{id}/publish", new PublishReleaseRequest(at, confirmed, url), Ct);

    private static async Task<EpisodeResponse> EpisodeAsync(HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/episodes/{id}", Ct)).DataAsync<EpisodeResponse>();

    private static async Task<List<CalendarItemResponse>> CalendarAsync(HttpClient client, DateOnly from, DateOnly to, string extra = "") =>
        await (await client.GetAsync(
                $"/api/v1/calendar?start={Uri.EscapeDataString(At(from, 0).ToString("O"))}&end={Uri.EscapeDataString(At(to, 0).ToString("O"))}{extra}", Ct))
            .DataAsync<List<CalendarItemResponse>>();

    [Fact]
    public async Task A_release_without_an_episode_is_typed_in_and_scheduled_like_any_other()
    {
        var admin = await factory.AdminClientAsync();
        var channels = await ChannelsAsync(admin);
        var title = $"Worship single {Guid.NewGuid():N}"[..23];
        var at = At(Day(20), 18);

        // Neither an episode nor a title: rejected by validation.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(null, channels["Spotify"], at, "Scheduled", null), Ct)).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(null, channels["Spotify"], at, "Scheduled", null, Title: $"  {title}  "), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var release = await created.DataAsync<ReleaseResponse>();
        Assert.Null(release.Episode);
        Assert.Equal(title, release.Title);
        Assert.Equal("Scheduled", release.Status);

        // A second channel for the same title is fine: only episodes are limited to one live release per channel.
        var youtube = await ScheduleTitledAsync(admin, title, channels["YouTube"], at);
        Assert.Equal(title, youtube.Title);

        // On the calendar, in search and in the release list like any other release.
        var items = await CalendarAsync(admin, Day(20), Day(21), "&sourceTypes=Release");
        Assert.Contains(items, i => i.SourceId == release.Id && i.Title == $"Spotify · {title}");
        var found = await (await admin.GetAsync($"/api/v1/releases?search={Uri.EscapeDataString(title)}", Ct))
            .DataAsync<PagedResponse<ReleaseResponse>>();
        Assert.Equal(2, found.Items.Count);
        var search = await (await admin.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(title)}", Ct)).DataAsync<List<SearchResultResponse>>();
        Assert.Contains(search, r => r.Id == release.Id && r.Title == $"Spotify · {title}");

        // Rescheduling and publishing work without an episode.
        var moved = await (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule",
            new RescheduleReleaseRequest(At(Day(21), 9)), Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal(At(Day(21), 9), moved.ScheduledAt);
        var published = await PublishAsync(admin, youtube.Id, DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("Published", (await published.DataAsync<ReleaseResponse>()).Status);
    }

    private static async Task<ReleaseResponse> ScheduleTitledAsync(HttpClient client, string title, Guid channelId, DateTimeOffset at) =>
        await (await client.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(null, channelId, at, "Scheduled", null, Title: title), Ct)).DataAsync<ReleaseResponse>();

    [Fact]
    public async Task The_specified_channels_are_seeded_and_managed_with_settings_permission()
    {
        var admin = await factory.AdminClientAsync();
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);
        var channels = await (await producer.GetAsync("/api/v1/distribution-channels", Ct)).DataAsync<List<DistributionChannelResponse>>();
        foreach (var name in new[] { "YouTube", "Spotify", "Facebook", "Instagram", "Television", "Website", "Other" })
        {
            Assert.Contains(channels, c => c.Name == name && c.IsActive);
        }
        Assert.Equal("Other", channels.Last().Name);

        Assert.Equal(HttpStatusCode.Forbidden, (await producer.PostAsJsonAsync("/api/v1/distribution-channels",
            new DistributionChannelRequest("Podcast app", "Audio", null), Ct)).StatusCode);
        var name2 = $"Radio {Guid.NewGuid():N}"[..14];
        var created = await admin.PostAsJsonAsync("/api/v1/distribution-channels", new DistributionChannelRequest(name2, "Broadcast", null), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var radio = await created.DataAsync<DistributionChannelResponse>();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/distribution-channels",
            new DistributionChannelRequest(name2.ToUpperInvariant(), "Broadcast", null), Ct)).StatusCode);

        // Deactivated channels drop out of the list and take no new releases.
        await admin.PutAsJsonAsync($"/api/v1/distribution-channels/{radio.Id}", new DistributionChannelRequest(name2, "Broadcast", null, false), Ct);
        Assert.DoesNotContain(await ChannelsAsync(admin), c => c.Value == radio.Id);
        var (_, episode) = await EpisodeAsync(admin, "Inactive channel show");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, radio.Id, null, "Draft", null), Ct)).StatusCode);
    }

    [Fact]
    public async Task One_episode_has_independent_release_dates_and_statuses_across_channels()
    {
        var admin = await factory.AdminClientAsync();
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var channels = await ChannelsAsync(admin);
        var (show, episode) = await EpisodeAsync(admin, "Multi-channel show");

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channels["YouTube"], At(Day(1), 18), "Scheduled", null), Ct)).StatusCode);
        var youtube = await ScheduleAsync(admin, episode.Id, channels["YouTube"], At(Day(1), 18));
        var spotify = await ScheduleAsync(admin, episode.Id, channels["Spotify"], At(Day(1), 6));
        var tv = await ScheduleAsync(admin, episode.Id, channels["Television"], At(Day(3), 20), status: "AwaitingApproval");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channels["YouTube"], At(Day(2), 9), "Scheduled", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channels["Website"], null, "Scheduled", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channels["Website"], null, "Published", null), Ct)).StatusCode);

        var schedule = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}/releases", Ct)).DataAsync<List<ReleaseResponse>>();
        Assert.Equal(["Spotify", "YouTube", "Television"], schedule.Select(r => r.Channel.Name));
        Assert.Equal(["Scheduled", "Scheduled", "AwaitingApproval"], schedule.Select(r => r.Status));

        // Each release is on the calendar at its own time, opening the release.
        var items = await CalendarAsync(admin, Day(0), Day(5), $"&showId={show.Id}&eventTypes=Release");
        Assert.Equal([spotify.Id, youtube.Id, tv.Id], items.Select(i => i.SourceId));
        Assert.Equal("YouTube · Multi-channel show S1 E1: Grace", items[1].Title);

        // Publishing one channel does not touch the others; the episode follows its releases.
        var publishedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.Equal(HttpStatusCode.OK, (await PublishAsync(admin, spotify.Id, publishedAt)).StatusCode);
        var detail = await EpisodeAsync(admin, episode.Id);
        Assert.Equal("PartiallyPublished", detail.Status);
        Assert.Equal(["Published", "Scheduled", "AwaitingApproval"], detail.Releases.Select(r => r.Status));

        await PublishAsync(admin, youtube.Id, publishedAt);
        await admin.PatchAsJsonAsync($"/api/v1/releases/{tv.Id}/cancel", new CancelReleaseRequest("Broadcaster dropped the slot"), Ct);
        detail = await EpisodeAsync(admin, episode.Id);
        Assert.Equal("Published", detail.Status);
        Assert.Equal("Done", detail.Timeline.Last().State);
        var history = await (await admin.GetAsync($"/api/v1/episodes/{episode.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary == "Status changed from PartiallyPublished to Published: Follows its releases");

        // A new channel makes the episode partially published again.
        await ScheduleAsync(admin, episode.Id, channels["Facebook"], At(Day(4), 12));
        Assert.Equal("PartiallyPublished", (await EpisodeAsync(admin, episode.Id)).Status);
    }

    [Fact]
    public async Task Publication_needs_confirmation_and_keeps_both_scheduled_and_actual_times()
    {
        var admin = await factory.AdminClientAsync();
        var (assignee, assigneeClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var (_, outsider) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var channels = await ChannelsAsync(admin);
        var (_, episode) = await EpisodeAsync(admin, "Confirmation show");
        var scheduled = DateTimeOffset.UtcNow.AddHours(-3);
        var release = await ScheduleAsync(admin, episode.Id, channels["YouTube"], scheduled, assignee.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(assigneeClient, release.Id, DateTimeOffset.UtcNow, confirmed: false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PublishAsync(assigneeClient, release.Id, DateTimeOffset.UtcNow.AddHours(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PublishAsync(outsider, release.Id, DateTimeOffset.UtcNow)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(assigneeClient, release.Id, DateTimeOffset.UtcNow, url: "not a link")).StatusCode);

        // The assignee confirms; both times are kept.
        var actual = scheduled.AddMinutes(47);
        var published = await (await PublishAsync(assigneeClient, release.Id, actual, url: "https://youtu.be/abc"))
            .DataAsync<ReleaseResponse>();
        Assert.Equal(("Published", actual, scheduled), (published.Status, published.ActualPublishedAt!.Value, published.ScheduledAt!.Value));
        Assert.Equal(assignee.Id, published.PublishedBy!.Id);
        Assert.Equal("https://youtu.be/abc", published.ExternalUrl);
        Assert.False(published.IsOverdue);
        Assert.False(published.CanEdit);

        // A published release keeps its schedule and cannot be cancelled; re-confirming corrects the time.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule",
            new RescheduleReleaseRequest(At(Day(9), 9)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/cancel",
            new CancelReleaseRequest("Oops"), Ct)).StatusCode);
        var corrected = await (await PublishAsync(admin, release.Id, actual.AddMinutes(-2))).DataAsync<ReleaseResponse>();
        Assert.Equal(actual.AddMinutes(-2), corrected.ActualPublishedAt);
        Assert.Equal("https://youtu.be/abc", corrected.ExternalUrl);

        var history = await (await admin.GetAsync($"/api/v1/releases/{release.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary.StartsWith("Publication confirmed: published ") && h.User!.Id == assignee.Id);
        Assert.Contains(history, h => h.Summary.StartsWith("Publication time corrected from "));
    }

    [Fact]
    public async Task Missed_releases_are_flagged_but_never_published_automatically()
    {
        var admin = await factory.AdminClientAsync();
        var (owner, ownerClient) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var channels = await ChannelsAsync(admin);
        var (show, episode) = await EpisodeAsync(admin, "Missed release show");
        var missedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var missed = await ScheduleAsync(admin, episode.Id, channels["Instagram"], missedAt, owner.Id);
        var future = await ScheduleAsync(admin, episode.Id, channels["Website"], DateTimeOffset.UtcNow.AddDays(2), owner.Id);

        var read = await (await ownerClient.GetAsync($"/api/v1/releases/{missed.Id}", Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal(("Scheduled", "Overdue", true), (read.Status, read.DisplayStatus, read.IsOverdue));
        Assert.Null(read.ActualPublishedAt);
        Assert.Equal("ReadyForRelease", (await EpisodeAsync(admin, episode.Id)).Status);

        var overdue = await (await ownerClient.GetAsync("/api/v1/releases/overdue?mine=true", Ct)).DataAsync<List<ReleaseResponse>>();
        Assert.Equal(missed.Id, Assert.Single(overdue).Id);
        var view = await (await admin.GetAsync($"/api/v1/releases?view=Overdue&showId={show.Id}", Ct)).DataAsync<PagedResponse<ReleaseResponse>>();
        Assert.Equal(missed.Id, Assert.Single(view.Items).Id);
        view = await (await admin.GetAsync($"/api/v1/releases?view=Scheduled&showId={show.Id}", Ct)).DataAsync<PagedResponse<ReleaseResponse>>();
        Assert.Equal(future.Id, Assert.Single(view.Items).Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/releases?view=Soon", Ct)).StatusCode);

        var dashboard = await (await ownerClient.GetAsync("/api/v1/dashboard/overdue?mine=true", Ct)).DataAsync<OverdueResponse>();
        Assert.Equal(missed.Id, Assert.Single(dashboard.Releases).Id);
        var today = DateOnly.FromDateTime(missedAt.UtcDateTime);
        var items = await CalendarAsync(admin, today.AddDays(-1), today.AddDays(2), $"&showId={show.Id}&statuses=Overdue");
        Assert.Equal(missed.Id, Assert.Single(items).SourceId);

        // Rescheduling clears the flag.
        var moved = await (await admin.PatchAsJsonAsync($"/api/v1/releases/{missed.Id}/schedule",
            new RescheduleReleaseRequest(DateTimeOffset.UtcNow.AddDays(1)), Ct)).DataAsync<ReleaseResponse>();
        Assert.False(moved.IsOverdue);
        Assert.Empty(await (await ownerClient.GetAsync("/api/v1/releases/overdue?mine=true", Ct)).DataAsync<List<ReleaseResponse>>());
    }

    [Fact]
    public async Task Rescheduling_and_postponing_move_the_release_and_keep_the_previous_date_in_history()
    {
        var admin = await factory.AdminClientAsync();
        var channels = await ChannelsAsync(admin);
        var (show, episode) = await EpisodeAsync(admin, "Postponed show");
        var release = await ScheduleAsync(admin, episode.Id, channels["Television"], At(Day(10), 20));

        var moved = await (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule",
            new RescheduleReleaseRequest(At(Day(11), 21), release.Version), Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal(At(Day(11), 21), moved.ScheduledAt);
        var items = await CalendarAsync(admin, Day(9), Day(14), $"&showId={show.Id}");
        Assert.Equal(At(Day(11), 21), Assert.Single(items).StartAt);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule",
            new RescheduleReleaseRequest(At(Day(12), 21), release.Version), Ct)).StatusCode);

        // Postponing needs a reason; without a new date it leaves the calendar and waits in the Postponed view.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/postpone",
            new PostponeReleaseRequest(""), Ct)).StatusCode);
        var postponed = await (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/postpone",
            new PostponeReleaseRequest("Broadcaster schedule change"), Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal(("Postponed", (DateTimeOffset?)null, "Broadcaster schedule change"),
            (postponed.Status, postponed.ScheduledAt, postponed.PostponementReason));
        Assert.Empty(await CalendarAsync(admin, Day(9), Day(14), $"&showId={show.Id}"));
        var view = await (await admin.GetAsync($"/api/v1/releases?view=Postponed&showId={show.Id}", Ct)).DataAsync<PagedResponse<ReleaseResponse>>();
        Assert.Single(view.Items);

        // A new time schedules it again.
        var again = await (await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/schedule",
            new RescheduleReleaseRequest(At(Day(13), 20)), Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal("Scheduled", again.Status);

        var history = (await (await admin.GetAsync($"/api/v1/releases/{release.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>())
            .Select(h => h.Summary).ToList();
        Assert.Contains(history, h => h.StartsWith("Release time changed from ") && h.Contains(At(Day(10), 20).ToString("yyyy-MM-dd")));
        Assert.Contains(history, h => h.StartsWith("Postponed from ") && h.EndsWith("to further notice: Broadcaster schedule change"));
        Assert.Contains("Status changed from Postponed to Scheduled", history);
    }

    [Fact]
    public async Task Workflow_statuses_are_edited_and_unpublished_content_is_reported()
    {
        var admin = await factory.AdminClientAsync();
        var channels = await ChannelsAsync(admin);
        var (_, episode) = await EpisodeAsync(admin, "Unpublished show");
        var (_, bare) = await EpisodeAsync(admin, "No releases show");
        var draft = await ScheduleAsync(admin, episode.Id, channels["Website"], null, status: "Draft");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"/api/v1/releases/{draft.Id}",
            new UpdateReleaseRequest("Scheduled", null, null, null, null, draft.Version), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/releases/{draft.Id}",
            new UpdateReleaseRequest("Published", null, null, null, null, draft.Version), Ct)).StatusCode);
        var ready = await (await admin.PutAsJsonAsync($"/api/v1/releases/{draft.Id}",
            new UpdateReleaseRequest("Ready", null, null, null, "Thumbnail approved", draft.Version), Ct)).DataAsync<ReleaseResponse>();
        Assert.Equal(("Ready", "Thumbnail approved"), (ready.Status, ready.Notes));

        var unpublished = await (await admin.GetAsync("/api/v1/releases/unpublished", Ct)).DataAsync<UnpublishedContentResponse>();
        Assert.Contains(unpublished.Releases, r => r.Id == draft.Id);
        Assert.Contains(unpublished.EpisodesWithoutReleases, e => e.Id == bare.Id);
        Assert.DoesNotContain(unpublished.EpisodesWithoutReleases, e => e.Id == episode.Id);

        var history = await (await admin.GetAsync($"/api/v1/releases/{draft.Id}/history", Ct)).DataAsync<List<ActivityEntryResponse>>();
        Assert.Contains(history, h => h.Summary == "Status changed from Draft to Ready");

        // Cancelled episodes take no releases.
        await admin.PatchAsJsonAsync($"/api/v1/episodes/{bare.Id}/status", new ChangeEpisodeStatusRequest("Cancelled"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(bare.Id, channels["YouTube"], null, "Draft", null), Ct)).StatusCode);
    }
}
