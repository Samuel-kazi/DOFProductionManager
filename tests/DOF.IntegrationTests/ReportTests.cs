using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Rules;
using DOF.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class ReportTests(DofApiFactory factory)
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static async Task<ReportResponse> ReportAsync(HttpClient client, string path) =>
        await (await client.GetAsync($"/api/v1/reports/{path}", Ct)).DataAsync<ReportResponse>();

    private static int Col(ReportResponse r, string name) => r.Columns.ToList().IndexOf(name);

    private static async Task<(EpisodeResponse Episode, Guid Channel)> EpisodeAsync(HttpClient admin, string show)
    {
        var s = await (await admin.PostAsJsonAsync("/api/v1/shows", new CreateShowRequest(show, null, "OneOff", null, null), Ct))
            .DataAsync<ShowResponse>();
        var e = await (await admin.PostAsJsonAsync($"/api/v1/shows/{s.Id}/episodes",
            new CreateEpisodeRequest("Pilot", null, null, null, null, null, null, null, null, null), Ct)).DataAsync<EpisodeResponse>();
        var channel = (await (await admin.GetAsync("/api/v1/distribution-channels", Ct)).DataAsync<List<DistributionChannelResponse>>())
            .Single(c => c.Name == "Spotify").Id;
        return (e, channel);
    }

    [Fact]
    public async Task Reports_need_reports_view_and_describe_the_live_records()
    {
        var admin = await factory.AdminClientAsync();
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);
        foreach (var (key, _) in ReportValues.Reports)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/v1/reports/{key}", Ct)).StatusCode);
        }

        var unique = Guid.NewGuid().ToString("N")[..6];
        var done = await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest($"Done {unique}", null, null, Now.AddHours(1), "Normal", null, null), Ct)).DataAsync<TaskResponse>();
        await admin.PatchAsJsonAsync($"/api/v1/tasks/{done.Id}/status", new ChangeStatusRequest("Completed"), Ct);
        var late = await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest($"Late {unique}", null, null, Now.AddDays(-3), "Urgent", null, null), Ct)).DataAsync<TaskResponse>();
        var (episode, channel) = await EpisodeAsync(admin, $"Report show {unique}");
        await admin.PatchAsJsonAsync($"/api/v1/episodes/{episode.Id}/status", new ChangeEpisodeStatusRequest("Approved"), Ct);
        var release = await (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channel, Now.AddHours(-2), "Scheduled", null), Ct)).DataAsync<ReleaseResponse>();
        await admin.PatchAsJsonAsync($"/api/v1/releases/{release.Id}/publish", new PublishReleaseRequest(Now.AddHours(-1), true), Ct);
        var (upcomingEpisode, _) = await EpisodeAsync(admin, $"Upcoming show {unique}");
        await admin.PatchAsJsonAsync($"/api/v1/episodes/{upcomingEpisode.Id}/status", new ChangeEpisodeStatusRequest("Recorded"), Ct);
        var upcoming = await (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(upcomingEpisode.Id, channel, Now.AddDays(3), "Scheduled", null), Ct)).DataAsync<ReleaseResponse>();

        var weekly = await ReportAsync(admin, "weekly");
        Assert.Equal(["Activity", "Item", "When", "Status", "Responsible"], weekly.Columns);
        Assert.Contains(weekly.Rows, r => r[0] == "Task completed" && r[1] == $"Done {unique}");
        Assert.Contains(weekly.Rows, r => r[0] == "Release published" && r[1].Contains($"Report show {unique}"));
        Assert.Contains(weekly.Rows, r => r[0] == "Episode recorded" && r[1].Contains($"Upcoming show {unique}"));
        Assert.Equal(weekly.Rows.Count, weekly.Links.Count);
        Assert.Contains(weekly.Summary, f => f.Label == "Releases published");
        Assert.Contains((await ReportAsync(admin, "monthly")).Rows, r => r[1] == $"Done {unique}");

        var upcomingReport = await ReportAsync(admin, "upcoming-releases?days=7");
        Assert.Contains(upcomingReport.Links, l => l?.SourceId == upcoming.Id);
        var unpublished = await ReportAsync(admin, "unpublished-episodes");
        var row = unpublished.Rows[unpublished.Links.ToList().FindIndex(l => l?.SourceId == upcomingEpisode.Id)];
        Assert.Equal("0 of 1", row[Col(unpublished, "Published")]);
        Assert.DoesNotContain(unpublished.Links, l => l?.SourceId == episode.Id);

        var overdue = await ReportAsync(admin, "overdue-tasks");
        var lateRow = overdue.Rows[overdue.Links.ToList().FindIndex(l => l?.SourceId == late.Id)];
        Assert.Equal(("3", "Urgent"), (lateRow[Col(overdue, "Days overdue")], lateRow[Col(overdue, "Priority")]));

        var history = await ReportAsync(admin, $"publication-history?from={DateOnly.FromDateTime(Now.UtcDateTime.AddDays(-1)):yyyy-MM-dd}");
        var published = history.Rows[history.Links.ToList().FindIndex(l => l?.SourceId == release.Id)];
        Assert.Equal("60", published[Col(history, "Delay (min)")]);
        Assert.Equal("System Administrator", published[Col(history, "Confirmed by")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.GetAsync("/api/v1/reports/publication-history?from=2030-01-01&to=2029-01-01", Ct)).StatusCode);

        Assert.Contains((await ReportAsync(admin, "team-workload")).Rows, r => r[0] == "System Administrator");
        Assert.NotEmpty((await ReportAsync(admin, "project-status")).Columns);

        // Times are formatted in the requested timezone.
        var utc = await ReportAsync(admin, "upcoming-releases?days=7&timeZone=UTC");
        Assert.Equal("UTC", utc.TimeZone);
        Assert.Equal(upcoming.ScheduledAt!.Value.UtcDateTime.ToString(ReportValues.DateTimeFormat),
            utc.Rows[utc.Links.ToList().FindIndex(l => l?.SourceId == upcoming.Id)][0]);
    }

    [Fact]
    public async Task Reports_export_as_safe_csv()
    {
        var admin = await factory.AdminClientAsync();
        var tricky = $"=HYPERLINK(\"x\"), \"quoted\" {Guid.NewGuid():N}"[..40];
        await admin.PostAsJsonAsync("/api/v1/tasks", new CreateTaskRequest(tricky, null, null, Now.AddDays(-1), "Normal", null, null), Ct);

        var response = await admin.GetAsync("/api/v1/reports/overdue-tasks?format=csv", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("dof-overdue-tasks-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal("Task,Due,Days overdue,Responsible,Belongs to,Priority,Status", lines[0]);
        // A formula-like cell is neutralised with an apostrophe and quoted because of the comma and quotes.
        var expected = "\"'" + tricky.Replace("\"", "\"\"") + "\"";
        Assert.Contains(lines, l => l.StartsWith(expected + ","));
    }

    [Fact]
    public async Task Global_search_finds_records_of_every_kind_and_opens_them()
    {
        var admin = await factory.AdminClientAsync();
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var term = $"zebra{Guid.NewGuid():N}"[..11];
        var project = await (await admin.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest($"Project {term}", null, "Other", "Normal", null, null, null), Ct)).DataAsync<ProjectResponse>();
        var task = await (await admin.PostAsJsonAsync("/api/v1/tasks",
            new CreateTaskRequest($"Task {term}", null, null, null, "Normal", null, null), Ct)).DataAsync<TaskResponse>();
        var (episode, channel) = await EpisodeAsync(admin, $"Show {term}");
        await admin.PutAsJsonAsync($"/api/v1/episodes/{episode.Id}",
            new UpdateEpisodeRequest($"Episode {term}", null, null, null, null, null, null, null, null, null, null, episode.Version), Ct);
        var release = await (await admin.PostAsJsonAsync("/api/v1/releases",
            new CreateReleaseRequest(episode.Id, channel, null, "Draft", null), Ct)).DataAsync<ReleaseResponse>();
        var ev = await (await admin.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
            $"Event {term}", null, "Meeting", Now.AddDays(1), Now.AddDays(1).AddHours(1), false, null, null, "Normal", []), Ct))
            .DataAsync<CalendarEventResponse>();

        var hits = await (await member.GetAsync($"/api/v1/search?q={term.ToUpperInvariant()}", Ct)).DataAsync<List<SearchResultResponse>>();
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("Project", project.Id));
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("Task", task.Id));
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("Show", episode.Show.Id));
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("Episode", episode.Id));
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("Release", release.Id));
        Assert.Contains(hits, h => (h.SourceType, h.Id) == ("CalendarEvent", ev.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await member.GetAsync("/api/v1/search?q=a", Ct)).StatusCode);
    }

    [Fact]
    public async Task People_change_their_own_name_and_timezone()
    {
        var (user, client) = await factory.UserClientAsync(SystemRoles.TeamMember);
        var me = await (await client.PatchAsJsonAsync("/api/v1/auth/me", new UpdateMyProfileRequest("Grace Wanjiru", "Europe/London"), Ct))
            .DataAsync<CurrentUserResponse>();
        Assert.Equal(("Grace Wanjiru", "Europe/London", user.Email), (me.FullName, me.TimeZone, me.Email));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PatchAsJsonAsync("/api/v1/auth/me", new UpdateMyProfileRequest("Grace", "Mars/Olympus"), Ct)).StatusCode);
        var day = await (await client.GetAsync("/api/v1/calendar/day", Ct)).DataAsync<CalendarRangeResponse>();
        Assert.Equal("Europe/London", day.TimeZone);
    }

    /// <summary>
    /// Spec Part 9 targets on a realistic volume (on top of everything the other tests created): dashboard
    /// and calendar under 2 s, task listing under 1.5 s, episode details under 2 s, search under 1.5 s.
    /// </summary>
    [Fact]
    public async Task Main_screens_meet_the_performance_targets_with_a_realistic_volume()
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me", Ct)).DataAsync<CurrentUserResponse>();
        Guid episodeId = default;
        await factory.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<DofDbContext>();
            var start = new DateTimeOffset(2035, 3, 5, 6, 0, 0, TimeSpan.Zero);
            var channels = db.DistributionChannels.ToList();
            for (var p = 0; p < 40; p++)
            {
                var project = Project.Create($"Perf project {p}", null, ProjectType.Campaign, Priority.Normal, me.Id, null, null);
                db.Projects.Add(project);
                for (var m = 0; m < 5; m++)
                {
                    db.ProjectMilestones.Add(ProjectMilestone.Create(project.Id, $"Perf milestone {p}-{m}", null,
                        DateOnly.FromDateTime(start.UtcDateTime).AddDays(p % 28 + m), Priority.Normal, me.Id));
                }
            }
            for (var t = 0; t < 400; t++)
            {
                var task = WorkTask.Create($"Perf task {t}", null, null, null, start.AddHours(t * 3), Priority.Normal);
                task.SetAssignees([me.Id], me.Id, start);
                db.Tasks.Add(task);
            }
            for (var e = 0; e < 150; e++)
            {
                var ev = CalendarEvent.Create($"Perf event {e}", null, CalendarEventType.Meeting, start.AddHours(e * 4),
                    start.AddHours(e * 4 + 1), false, null, null, Priority.Normal);
                db.CalendarEvents.Add(ev);
            }
            for (var s = 0; s < 10; s++)
            {
                var show = Show.Create($"Perf show {s}", null, ShowType.Recurring, me.Id, 60);
                db.Shows.Add(show);
                var season = Season.Create(show.Id, 1, null, null, null);
                db.Seasons.Add(season);
                for (var n = 1; n <= 12; n++)
                {
                    var episode = Episode.Create(show, season, n, $"Perf episode {s}-{n}", null, me.Id);
                    episode.ScheduleRecording(start.AddDays(n + s), null, "Studio", 60);
                    episode.Assign(me.Id, EpisodeResponsibility.Producer, start);
                    db.Episodes.Add(episode);
                    episodeId = episode.Id;
                    foreach (var channel in channels.Take(3))
                    {
                        db.Releases.Add(Release.Create(episode, null, channel, null, start.AddDays(n + s + 7), ReleaseStatus.Scheduled, me.Id, null, null));
                    }
                }
            }
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        async Task<TimeSpan> TimeAsync(string path)
        {
            (await admin.GetAsync(path, Ct)).EnsureSuccessStatusCode(); // warm-up (JIT, connection pool)
            var watch = Stopwatch.StartNew();
            (await admin.GetAsync(path, Ct)).EnsureSuccessStatusCode();
            return watch.Elapsed;
        }

        Assert.InRange(await TimeAsync("/api/v1/dashboard/personal"), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.InRange(await TimeAsync("/api/v1/dashboard/management?start=2035-03-05"), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.InRange(await TimeAsync("/api/v1/calendar/month?year=2035&month=3"), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.InRange(await TimeAsync("/api/v1/tasks?open=true&pageSize=100"), TimeSpan.Zero, TimeSpan.FromSeconds(1.5));
        Assert.InRange(await TimeAsync($"/api/v1/episodes/{episodeId}"), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.InRange(await TimeAsync("/api/v1/search?q=perf"), TimeSpan.Zero, TimeSpan.FromSeconds(1.5));
        Assert.InRange(await TimeAsync("/api/v1/reports/weekly"), TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }
}
