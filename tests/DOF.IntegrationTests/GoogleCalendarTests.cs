using System.Net;
using System.Net.Http.Json;
using System.Web;
using DOF.Application.Common.Abstractions;
using DOF.Application.GoogleCalendar;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using DOF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DOF.IntegrationTests.TestApi;

namespace DOF.IntegrationTests;

public class GoogleCalendarTests(DofApiFactory factory)
{
    private const string Redirect = "http://127.0.0.1:53682/";
    private const string Base = "/api/v1/integrations/google-calendar";

    private FakeGoogleCalendarApi Google => factory.Google;

    private static DateTimeOffset Day(int n) => new DateTimeOffset(2032, 3, 1, 9, 0, 0, TimeSpan.Zero).AddDays(n);

    /// <summary>Runs the sign-in the way the desktop does: ask for the URL, then hand back Google's code and state.</summary>
    public static async Task<GoogleCalendarStatusResponse> ConnectAsync(DofApiFactory factory, HttpClient admin, string account = "dof.calendar@gmail.com")
    {
        factory.Google.NextAccount = account;
        factory.Google.FailWith = null;
        var authorize = await (await admin.PostAsJsonAsync($"{Base}/authorize", new StartGoogleAuthorizationRequest(Redirect), Ct))
            .DataAsync<GoogleAuthorizationResponse>();
        var state = HttpUtility.ParseQueryString(new Uri(authorize.AuthorizationUrl).Query)["state"]!;
        return await (await admin.PostAsJsonAsync($"{Base}/connect",
            new CompleteGoogleAuthorizationRequest(FakeGoogleCalendarApi.GoodCode, state), Ct)).DataAsync<GoogleCalendarStatusResponse>();
    }

    private async Task<CalendarEventResponse> EventAsync(HttpClient client, string title, DateTimeOffset start, bool allDay = false) =>
        await (await client.PostAsJsonAsync("/api/v1/calendar/events", new CreateCalendarEventRequest(
                title, "Bring your notes", "Meeting", start, allDay ? start.AddDays(1) : start.AddHours(2), allDay, "Studio A",
                "https://meet.example.org/abc", "Normal", []), Ct))
            .DataAsync<CalendarEventResponse>();

    private static string InvitesPath(Guid eventId) => $"/api/v1/calendar/events/{eventId}/google-invites";

    private Task<int> SyncAsync(DateTimeOffset now) =>
        factory.InScopeAsync(sp => sp.GetRequiredService<GoogleInviteSync>().RunAsync(now, Ct));

    private async Task<string> GoogleIdAsync(Guid eventId) =>
        await factory.InScopeAsync(sp => sp.GetRequiredService<DofDbContext>().GoogleCalendarInvites
            .Where(i => i.CalendarEventId == eventId).Select(i => i.GoogleEventId).SingleAsync(Ct));

    [Fact]
    public async Task An_administrator_connects_a_google_account_and_the_refresh_token_is_stored_encrypted()
    {
        var admin = await factory.AdminClientAsync();

        var status = await ConnectAsync(factory, admin, "media@dof.example");

        Assert.True(status.Connected);
        Assert.Equal("media@dof.example", status.AccountEmail);
        Assert.Equal(Redirect, Google.LastRedirectUri);
        Assert.False(string.IsNullOrEmpty(Google.LastVerifier));
        var stored = await factory.InScopeAsync(sp => sp.GetRequiredService<DofDbContext>().GoogleCalendarConnections
            .SingleAsync(c => c.ArchivedAt == null, Ct));
        Assert.DoesNotContain("refresh-", stored.EncryptedRefreshToken);
        var plain = await factory.InScopeAsync(sp => Task.FromResult(sp.GetRequiredService<ISecretProtector>()
            .Unprotect(stored.EncryptedRefreshToken, GoogleConnectionService.RefreshTokenPurpose)));
        Assert.Equal("refresh-media@dof.example", plain);

        // Connecting again replaces the previous account; only one stays active.
        await ConnectAsync(factory, admin, "second@dof.example");
        var active = await factory.InScopeAsync(sp => sp.GetRequiredService<DofDbContext>().GoogleCalendarConnections
            .Where(c => c.ArchivedAt == null).Select(c => c.AccountEmail).ToListAsync(Ct));
        Assert.Equal(["second@dof.example"], active);
    }

    [Fact]
    public async Task The_sign_in_state_cannot_be_forged_or_completed_by_someone_else()
    {
        var admin = await factory.AdminClientAsync();
        var authorize = await (await admin.PostAsJsonAsync($"{Base}/authorize", new StartGoogleAuthorizationRequest(Redirect), Ct))
            .DataAsync<GoogleAuthorizationResponse>();
        var state = HttpUtility.ParseQueryString(new Uri(authorize.AuthorizationUrl).Query)["state"]!;

        var forged = await admin.PostAsJsonAsync($"{Base}/connect",
            new CompleteGoogleAuthorizationRequest(FakeGoogleCalendarApi.GoodCode, state[..^4] + "AAAA"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forged.StatusCode);

        var (other, otherAdmin) = await factory.UserClientAsync(SystemRoles.Administrator);
        try
        {
            var stolen = await otherAdmin.PostAsJsonAsync($"{Base}/connect",
                new CompleteGoogleAuthorizationRequest(FakeGoogleCalendarApi.GoodCode, state), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, stolen.StatusCode);
            Assert.Contains("someone else", await stolen.Content.ReadAsStringAsync(Ct));
        }
        finally
        {
            // Other tests rely on the bootstrap administrator being the only active one.
            (await admin.PatchAsJsonAsync($"/api/v1/users/{other.Id}/status", new UpdateUserStatusRequest(false), Ct)).EnsureSuccessStatusCode();
        }

        var badCode = await admin.PostAsJsonAsync($"{Base}/connect", new CompleteGoogleAuthorizationRequest("wrong", state), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badCode.StatusCode);
    }

    [Fact]
    public async Task Only_settings_managers_connect_and_the_redirect_must_be_a_loopback_address()
    {
        var admin = await factory.AdminClientAsync();
        var (_, producer) = await factory.UserClientAsync(SystemRoles.Producer);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await producer.PostAsJsonAsync($"{Base}/authorize", new StartGoogleAuthorizationRequest(Redirect), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await producer.DeleteAsync(Base, Ct)).StatusCode);
        var seen = await (await producer.GetAsync(Base, Ct)).DataAsync<GoogleCalendarStatusResponse>();
        Assert.False(seen.CanManage);

        foreach (var redirect in new[] { "https://evil.example/", "http://192.168.1.5:5000/", "http://127.0.0.1/" })
        {
            var response = await admin.PostAsJsonAsync($"{Base}/authorize", new StartGoogleAuthorizationRequest(redirect), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Sending_invites_puts_the_event_on_the_guests_google_calendars_with_their_answers()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var member = await factory.CreateUserAsync(SystemRoles.TeamMember);
        var calendarEvent = await EventAsync(admin, "Faith Frequency planning", Day(0));
        Google.Answers[member.Email] = "accepted";

        var invite = await (await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id),
            new SendGoogleInvitesRequest([member.Id], ["Guest.Speaker@Example.org"]), Ct)).DataAsync<GoogleInviteResponse>();

        Assert.Equal("Synced", invite.Status);
        Assert.Equal("dof.calendar@gmail.com", invite.AccountEmail);
        Assert.Equal(2, invite.Guests.Count);
        Assert.Equal("Accepted", invite.Guests.Single(g => g.UserId == member.Id).Response);
        Assert.Equal("Pending", invite.Guests.Single(g => g.Email == "guest.speaker@example.org").Response);

        var googleId = await GoogleIdAsync(calendarEvent.Id);
        var sent = Google.Events[googleId];
        Assert.Equal("Faith Frequency planning", sent.Summary);
        Assert.Equal(Day(0), sent.Start.DateTime);
        Assert.Equal(Day(0).AddHours(2), sent.End.DateTime);
        Assert.Equal("Africa/Nairobi", sent.Start.TimeZone);
        Assert.Equal("Studio A", sent.Location);
        Assert.Contains("https://meet.example.org/abc", sent.Description);
        Assert.Equal(["guest.speaker@example.org", member.Email.ToLowerInvariant()], sent.Attendees.Select(a => a.Email).Order());
        Assert.Equal(["Insert"], Google.CallsFor(googleId).Select(c => c.Kind));

        // Recorded in the event's history.
        var actions = await factory.InScopeAsync(sp => sp.GetRequiredService<DofDbContext>().ActivityLogs
            .Where(a => a.EntityId == calendarEvent.Id).Select(a => a.Action).ToListAsync(Ct));
        Assert.Contains("GoogleInvitesSent", actions);
    }

    [Fact]
    public async Task Changing_the_event_in_dof_updates_everyones_google_calendar_once()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var calendarEvent = await EventAsync(admin, "Board meeting", Day(2));
        await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest(null, ["board@example.org"]), Ct);
        var googleId = await GoogleIdAsync(calendarEvent.Id);

        var moved = Day(3).AddHours(5);
        (await admin.PutAsJsonAsync($"/api/v1/calendar/events/{calendarEvent.Id}", new UpdateCalendarEventRequest(
            "Board meeting (moved)", calendarEvent.Description, "Meeting", moved, moved.AddHours(1), false, "Board room", null,
            "High", "Scheduled", [], calendarEvent.Version), Ct)).EnsureSuccessStatusCode();
        await SyncAsync(DateTimeOffset.UtcNow);
        await SyncAsync(DateTimeOffset.UtcNow);

        var kinds = Google.CallsFor(googleId).Select(c => c.Kind).ToList();
        Assert.Equal(["Insert", "Update"], kinds);
        var updated = Google.Events[googleId];
        Assert.Equal("Board meeting (moved)", updated.Summary);
        Assert.Equal(moved, updated.Start.DateTime);
        Assert.Equal("Board room", updated.Location);
    }

    [Fact]
    public async Task All_day_events_go_to_google_as_dates()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var calendarEvent = await EventAsync(admin, "Camp meeting", new DateTimeOffset(2032, 4, 6, 0, 0, 0, TimeSpan.FromHours(3)), allDay: true);

        await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest(null, ["camp@example.org"]), Ct);

        var sent = Google.Events[await GoogleIdAsync(calendarEvent.Id)];
        Assert.Equal(new DateOnly(2032, 4, 6), sent.Start.Date);
        Assert.Equal(new DateOnly(2032, 4, 7), sent.End.Date);
        Assert.Null(sent.Start.DateTime);
    }

    [Fact]
    public async Task Cancelling_the_event_or_removing_invites_takes_it_off_google_calendars()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var cancelled = await EventAsync(admin, "Training (cancelled)", Day(5));
        var removed = await EventAsync(admin, "Outreach (invites removed)", Day(6));
        foreach (var e in new[] { cancelled, removed })
        {
            await admin.PutAsJsonAsync(InvitesPath(e.Id), new SendGoogleInvitesRequest(null, ["team@example.org"]), Ct);
        }

        (await admin.DeleteAsync($"/api/v1/calendar/events/{cancelled.Id}", Ct)).EnsureSuccessStatusCode();
        await SyncAsync(DateTimeOffset.UtcNow);
        var afterCancel = await (await admin.GetAsync(InvitesPath(cancelled.Id), Ct)).DataAsync<GoogleInviteResponse>();
        Assert.Equal("Removed", afterCancel.Status);
        Assert.Equal("The event was cancelled.", afterCancel.LastError);
        Assert.False(Google.Events.ContainsKey(await GoogleIdAsync(cancelled.Id)));

        var afterRemove = await (await admin.DeleteAsync(InvitesPath(removed.Id), Ct)).DataAsync<GoogleInviteResponse>();
        Assert.Equal("Removed", afterRemove.Status);
        var removedId = await GoogleIdAsync(removed.Id);
        Assert.Equal("Delete", Google.CallsFor(removedId).Last().Kind);

        // Sending again puts it back.
        var again = await (await admin.PutAsJsonAsync(InvitesPath(removed.Id), new SendGoogleInvitesRequest(null, ["team@example.org"]), Ct))
            .DataAsync<GoogleInviteResponse>();
        Assert.Equal("Synced", again.Status);
        Assert.True(Google.Events.ContainsKey(removedId));

        // A cancelled event cannot be sent.
        var refused = await admin.PutAsJsonAsync(InvitesPath(cancelled.Id), new SendGoogleInvitesRequest(null, ["team@example.org"]), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task When_google_is_unavailable_the_invite_is_retried_until_it_succeeds()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var calendarEvent = await EventAsync(admin, "Retry me", Day(8));
        Google.FailWith = new HttpRequestException("Service unavailable");
        try
        {
            var invite = await (await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id),
                new SendGoogleInvitesRequest(null, ["retry@example.org"]), Ct)).DataAsync<GoogleInviteResponse>();
            Assert.Equal("Pending", invite.Status);
            Assert.Contains("could not be reached", invite.LastError);

            // Not due yet: the first retry waits a minute.
            await SyncAsync(DateTimeOffset.UtcNow);
            Assert.Equal("Pending", (await (await admin.GetAsync(InvitesPath(calendarEvent.Id), Ct)).DataAsync<GoogleInviteResponse>()).Status);
        }
        finally
        {
            Google.FailWith = null;
        }

        await SyncAsync(DateTimeOffset.UtcNow.AddMinutes(2));
        var synced = await (await admin.GetAsync(InvitesPath(calendarEvent.Id), Ct)).DataAsync<GoogleInviteResponse>();
        Assert.Equal("Synced", synced.Status);
        Assert.Null(synced.LastError);
    }

    [Fact]
    public async Task Revoked_google_access_is_reported_on_the_connection()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var calendarEvent = await EventAsync(admin, "Revoked", Day(9));
        Google.FailWith = new GoogleCalendarException("Google no longer accepts the connected account's access.", accessRevoked: true);
        try
        {
            var invite = await (await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id),
                new SendGoogleInvitesRequest(null, ["revoked@example.org"]), Ct)).DataAsync<GoogleInviteResponse>();
            Assert.Contains("Connect it again", invite.LastError);
            var status = await (await admin.GetAsync(Base, Ct)).DataAsync<GoogleCalendarStatusResponse>();
            Assert.Contains("Connect it again", status.LastError);
        }
        finally
        {
            Google.FailWith = null;
        }
    }

    [Fact]
    public async Task Reconnecting_the_same_account_keeps_updating_events_but_another_account_cannot()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin, "same@dof.example");
        var kept = await EventAsync(admin, "Same account", Day(10));
        await admin.PutAsJsonAsync(InvitesPath(kept.Id), new SendGoogleInvitesRequest(null, ["a@example.org"]), Ct);

        await ConnectAsync(factory, admin, "same@dof.example");
        await admin.PutAsJsonAsync(InvitesPath(kept.Id), new SendGoogleInvitesRequest(null, ["a@example.org", "b@example.org"]), Ct);
        var keptId = await GoogleIdAsync(kept.Id);
        Assert.Equal(["Insert", "Update"], Google.CallsFor(keptId).Select(c => c.Kind));

        var other = await EventAsync(admin, "Other account", Day(11));
        await admin.PutAsJsonAsync(InvitesPath(other.Id), new SendGoogleInvitesRequest(null, ["c@example.org"]), Ct);
        await ConnectAsync(factory, admin, "different@dof.example");
        (await admin.PutAsJsonAsync($"/api/v1/calendar/events/{other.Id}", new UpdateCalendarEventRequest(
            "Other account (renamed)", null, "Meeting", Day(11), Day(11).AddHours(2), false, null, null, "Normal", "Scheduled", [],
            other.Version), Ct)).EnsureSuccessStatusCode();
        await SyncAsync(DateTimeOffset.UtcNow);

        var failed = await (await admin.GetAsync(InvitesPath(other.Id), Ct)).DataAsync<GoogleInviteResponse>();
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("no longer connected", failed.LastError);
    }

    [Fact]
    public async Task Team_members_can_see_invites_but_not_send_them_and_sending_needs_a_connection()
    {
        var admin = await factory.AdminClientAsync();
        await ConnectAsync(factory, admin);
        var calendarEvent = await EventAsync(admin, "Visible invites", Day(12));
        var (_, member) = await factory.UserClientAsync(SystemRoles.TeamMember);

        Assert.Null(await (await member.GetAsync(InvitesPath(calendarEvent.Id), Ct)).DataAsync<GoogleInviteResponse?>());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest(null, ["x@example.org"]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest(null, ["not-an-email"]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest([], []), Ct)).StatusCode);

        (await admin.DeleteAsync(Base, Ct)).EnsureSuccessStatusCode();
        Assert.Contains(Google.Calls, c => c.Kind == "Revoke");
        var refused = await admin.PutAsJsonAsync(InvitesPath(calendarEvent.Id), new SendGoogleInvitesRequest(null, ["x@example.org"]), Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False((await (await admin.GetAsync(Base, Ct)).DataAsync<GoogleCalendarStatusResponse>()).Connected);
    }
}
