using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using DOF.Infrastructure.GoogleCalendar;
using Microsoft.Extensions.Options;

namespace DOF.UnitTests;

public class GoogleCalendarClientTests
{
    private static readonly IOptions<GoogleCalendarOptions> Options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        TokenEncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Plays Google: answers the token endpoint and records Calendar API requests.</summary>
    private sealed class FakeGoogle(Func<HttpRequestMessage, string?, HttpResponseMessage> calendar) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Requests { get; } = [];
        public int TokenRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.Parameter));
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                TokenRequests++;
                return Json(HttpStatusCode.OK, new JsonObject { ["access_token"] = $"access-{TokenRequests}", ["expires_in"] = 3600 });
            }
            return calendar(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static GoogleCalendarHttpApi Api(FakeGoogle handler) =>
        new(new HttpClient(handler), Options, new AesSecretProtector(Options));

    private static GoogleEventData Timed(string id = "dofabc123") => new(id, "Staff meeting", "Notes", "Board room",
        new GoogleEventTime(null, new DateTimeOffset(2032, 3, 1, 9, 0, 0, TimeSpan.FromHours(3)), "Africa/Nairobi"),
        new GoogleEventTime(null, new DateTimeOffset(2032, 3, 1, 10, 0, 0, TimeSpan.FromHours(3)), "Africa/Nairobi"),
        [new GoogleAttendee("a@example.org", "Ann"), new GoogleAttendee("b@example.org", null)]);

    [Fact]
    public void Timed_and_all_day_events_use_googles_formats()
    {
        var timed = GoogleCalendarHttpApi.EventBody(Timed(), new Dictionary<string, string> { ["a@example.org"] = "accepted" });
        Assert.Equal("2032-03-01T06:00:00Z", timed["start"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("Africa/Nairobi", timed["start"]!["timeZone"]!.GetValue<string>());
        Assert.Equal("confirmed", timed["status"]!.GetValue<string>());
        Assert.Equal("accepted", timed["attendees"]![0]!["responseStatus"]!.GetValue<string>());
        Assert.Null(timed["attendees"]![1]!["responseStatus"]);
        Assert.Null(timed["attendees"]![1]!["displayName"]);

        var allDay = GoogleCalendarHttpApi.EventBody(Timed() with
        {
            Start = new GoogleEventTime(new DateOnly(2032, 4, 6), null, "Africa/Nairobi"),
            End = new GoogleEventTime(new DateOnly(2032, 4, 7), null, "Africa/Nairobi"),
        }, new Dictionary<string, string>());
        Assert.Equal("2032-04-06", allDay["start"]!["date"]!.GetValue<string>());
        Assert.Equal("2032-04-07", allDay["end"]!["date"]!.GetValue<string>());
        Assert.Null(allDay["start"]!["dateTime"]);
    }

    [Fact]
    public async Task A_new_event_is_inserted_with_notifications_and_returns_answers()
    {
        var handler = new FakeGoogle((_, _) => Json(HttpStatusCode.OK, new JsonObject
        {
            ["attendees"] = new JsonArray(
                new JsonObject { ["email"] = "organiser@dof.example", ["organizer"] = true, ["responseStatus"] = "accepted" },
                new JsonObject { ["email"] = "a@example.org", ["responseStatus"] = "tentative" }),
        }));

        var answers = await Api(handler).UpsertEventAsync("refresh", "primary", Timed(), exists: false, Ct);

        var insert = handler.Requests.Single(r => r.Url.Contains("/events"));
        Assert.Equal(HttpMethod.Post, insert.Method);
        Assert.Equal("https://www.googleapis.com/calendar/v3/calendars/primary/events?sendUpdates=all", insert.Url);
        Assert.Equal("access-1", insert.Auth);
        Assert.Equal([new GoogleAttendeeResponse("a@example.org", "tentative")], answers);
    }

    [Fact]
    public async Task An_insert_that_already_happened_becomes_an_update_that_keeps_answers()
    {
        var handler = new FakeGoogle((request, _) => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Conflict, new JsonObject { ["error"] = new JsonObject { ["message"] = "duplicate" } })
            : Json(HttpStatusCode.OK, new JsonObject
            {
                ["attendees"] = new JsonArray(new JsonObject { ["email"] = "A@example.org", ["responseStatus"] = "accepted" }),
            }));

        await Api(handler).UpsertEventAsync("refresh-2", "primary", Timed("dofconflict1"), exists: false, Ct);

        var calls = handler.Requests.Where(r => r.Url.Contains("/events")).Select(r => r.Method).ToList();
        Assert.Equal([HttpMethod.Post, HttpMethod.Get, HttpMethod.Put], calls);
        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.EndsWith("/events/dofconflict1?sendUpdates=all", put.Url);
        Assert.Equal("accepted", JsonNode.Parse(put.Body!)!["attendees"]![0]!["responseStatus"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deleting_an_event_that_is_already_gone_succeeds_and_google_errors_are_reported()
    {
        var gone = new FakeGoogle((_, _) => new HttpResponseMessage(HttpStatusCode.Gone));
        await Api(gone).DeleteEventAsync("refresh-3", "primary", "dofgone", Ct);
        Assert.Contains(gone.Requests, r => r.Method == HttpMethod.Delete && r.Url.EndsWith("/events/dofgone?sendUpdates=all"));

        var refused = new FakeGoogle((_, _) => Json(HttpStatusCode.BadRequest,
            new JsonObject { ["error"] = new JsonObject { ["message"] = "Invalid attendee email." } }));
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => Api(refused).UpsertEventAsync("refresh-4", "primary", Timed(), false, Ct));
        Assert.Contains("Invalid attendee email.", ex.Message);

        var down = new FakeGoogle((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => Api(down).UpsertEventAsync("refresh-5", "primary", Timed(), false, Ct));
    }

    [Fact]
    public async Task An_expired_access_token_is_refreshed_once_and_a_second_rejection_means_access_was_revoked()
    {
        var attempts = 0;
        var handler = new FakeGoogle((_, _) => ++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Json(HttpStatusCode.OK, new JsonObject()));
        await Api(handler).GetResponsesAsync("refresh-6", "primary", "dofx", Ct);
        Assert.Equal(2, handler.TokenRequests);

        var revoked = new FakeGoogle((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => Api(revoked).GetResponsesAsync("refresh-7", "primary", "dofx", Ct));
        Assert.True(ex.AccessRevoked);
    }

    [Fact]
    public async Task Exchanging_the_code_sends_the_verifier_and_reads_the_account()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"media@dof.example\"}")).TrimEnd('=');
        var handler = new TokenEndpoint(new JsonObject
        {
            ["access_token"] = "a", ["expires_in"] = 3600, ["refresh_token"] = "r1",
            ["scope"] = $"openid email {GoogleCalendarHttpApi.CalendarScope}", ["id_token"] = $"h.{payload}.s",
        });

        var grant = await new GoogleCalendarHttpApi(new HttpClient(handler), Options, new AesSecretProtector(Options))
            .ExchangeCodeAsync("code", "http://127.0.0.1:5555/", "verifier-1", Ct);

        Assert.Equal(new GoogleTokenGrant("r1", "media@dof.example"), grant);
        Assert.Contains("code_verifier=verifier-1", handler.Body);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5555%2F", handler.Body);

        var noCalendar = new TokenEndpoint(new JsonObject { ["refresh_token"] = "r", ["scope"] = "openid email", ["id_token"] = $"h.{payload}.s" });
        await Assert.ThrowsAsync<GoogleCalendarException>(() =>
            new GoogleCalendarHttpApi(new HttpClient(noCalendar), Options, new AesSecretProtector(Options))
                .ExchangeCodeAsync("code", "http://127.0.0.1:5555/", "v", Ct));
    }

    [Fact]
    public void The_authorization_url_asks_for_offline_calendar_access_with_pkce()
    {
        var url = new Uri(Api(new FakeGoogle((_, _) => new HttpResponseMessage())).BuildAuthorizationUrl("http://127.0.0.1:5555/", "st", "ch"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("accounts.google.com", url.Host);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("ch", query["code_challenge"]);
        Assert.Contains(GoogleCalendarHttpApi.CalendarScope, query["scope"]);
    }

    private sealed class TokenEndpoint(JsonObject reply) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.OK, reply);
        }
    }
}

public class SecretProtectorAndInviteTests
{
    private static AesSecretProtector Protector(string? key = null) => new(Options.Create(new GoogleCalendarOptions
    {
        TokenEncryptionKey = key ?? Convert.ToBase64String(new byte[32]),
    }));

    [Fact]
    public void Secrets_round_trip_only_with_the_same_key_and_purpose()
    {
        var protector = Protector();
        var sealedValue = protector.Protect("refresh-token", "google-refresh-token");

        Assert.DoesNotContain("refresh-token", sealedValue);
        Assert.NotEqual(sealedValue, protector.Protect("refresh-token", "google-refresh-token"));
        Assert.Equal("refresh-token", protector.Unprotect(sealedValue, "google-refresh-token"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(sealedValue, "google-oauth-state"));
        Assert.ThrowsAny<CryptographicException>(() => Protector(Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()))
            .Unprotect(sealedValue, "google-refresh-token"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("not-valid", "google-refresh-token"));
        Assert.False(Protector("too-short").IsConfigured);
    }

    [Fact]
    public void Guest_lists_are_normalised_and_failures_stop_after_the_last_attempt()
    {
        var now = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var connection = Guid.NewGuid();
        var invite = new GoogleCalendarInvite(Guid.NewGuid(), connection, now);
        invite.SetGuests([("Ann@Example.org", null, "Ann"), ("ann@example.org", null, null), ("bob@example.org", Guid.NewGuid(), "Bob")], connection, now);

        Assert.Equal(["ann@example.org", "bob@example.org"], invite.Guests.Select(g => g.Email).Order());
        Assert.StartsWith("dof", invite.GoogleEventId);
        Assert.Throws<DomainException>(() => invite.SetGuests([], connection, now));

        for (var i = 1; i < GoogleCalendarInvite.MaxAttempts; i++)
        {
            invite.RecordFailure("down", now);
            Assert.Equal(GoogleInviteStatus.Pending, invite.Status);
        }
        invite.RecordFailure("down", now);
        Assert.Equal(GoogleInviteStatus.Failed, invite.Status);

        var firstId = invite.GoogleEventId;
        invite.SetGuests([("ann@example.org", null, null)], Guid.NewGuid(), now);
        Assert.Equal(GoogleInviteStatus.Pending, invite.Status);
        Assert.NotEqual(firstId, invite.GoogleEventId);
    }
}
