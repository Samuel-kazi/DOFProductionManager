using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.GoogleCalendar;

/// <summary>
/// Background job (every minute): pushes new and changed invites to Google, removes cancelled ones,
/// and reads guests' answers. The Google copy is always rebuilt from the calendar event, so editing the
/// event in DOF is what updates everyone's Google Calendar. Failures are recorded on the invite and
/// retried with growing delays.
/// </summary>
public sealed class GoogleInviteSync(
    IApplicationDbContext db,
    IGoogleCalendarApi google,
    ISecretProtector protector,
    IOptions<OrganisationOptions> organisation,
    ILogger<GoogleInviteSync> logger)
{
    public const int BatchSize = 100;
    public static readonly TimeSpan ResponseCheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NotConnectedDelay = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Returns the number of invites pushed to or removed from Google.</summary>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var connection = await db.GoogleCalendarConnections.SingleOrDefaultAsync(c => c.ArchivedAt == null, ct);

        // Queued work, plus synced invites whose event changed or that are due an answer check.
        var due = await db.GoogleCalendarInvites
            .Where(i => (i.Status == GoogleInviteStatus.Pending || i.Status == GoogleInviteStatus.Removing) && i.NextAttemptAt <= now)
            .OrderBy(i => i.NextAttemptAt).Select(i => i.Id).Take(BatchSize).ToListAsync(ct);
        var recentlyEnded = now.AddDays(-1);
        var synced = await db.GoogleCalendarInvites
            .Where(i => i.Status == GoogleInviteStatus.Synced
                        && db.CalendarEvents.Any(e => (e.Id == i.CalendarEventId || (i.SessionWeekId != null && e.SessionWeekId == i.SessionWeekId))
                                                      && e.EndAt >= recentlyEnded))
            .Select(i => i.Id).ToListAsync(ct);

        var pushed = 0;
        foreach (var id in due.Concat(synced).Distinct())
        {
            if (await ProcessAsync(id, connection, now, ct))
            {
                pushed++;
            }
        }
        return pushed;
    }

    /// <summary>Processes one invite now (used right after someone sends or removes invites).</summary>
    public async Task SyncOneAsync(Guid inviteId, DateTimeOffset now, CancellationToken ct)
    {
        var connection = await db.GoogleCalendarConnections.SingleOrDefaultAsync(c => c.ArchivedAt == null, ct);
        await ProcessAsync(inviteId, connection, now, ct);
    }

    /// <summary>What Google should hold for this event and guest list.</summary>
    public GoogleEventData Build(CalendarEvent e, GoogleCalendarInvite invite)
    {
        var zone = ZonedTime.Find(organisation.Value.DefaultTimeZone);
        var (start, end) = e.IsAllDay
            ? (new GoogleEventTime(ZonedTime.LocalDate(e.StartAt, zone), null, zone.Id),
               new GoogleEventTime(EndDate(e, zone), null, zone.Id))
            : (new GoogleEventTime(null, e.StartAt, zone.Id), new GoogleEventTime(null, e.EndAt, zone.Id));

        var description = new StringBuilder();
        if (e.Speaker is { } speaker)
        {
            description.AppendLine($"Sharing: {speaker}").AppendLine();
        }
        if (e.Description is { } text)
        {
            description.AppendLine(text).AppendLine();
        }
        if (e.MeetingUrl is { } url)
        {
            description.AppendLine($"Meeting link: {url}").AppendLine();
        }
        description.Append("Sent from DOF Production Manager.");

        return new GoogleEventData(invite.GoogleEventId, e.Title, description.ToString(), e.Location ?? e.MeetingUrl, start, end,
            [.. invite.Guests.OrderBy(g => g.Email, StringComparer.Ordinal).Select(g => new GoogleAttendee(g.Email, g.DisplayName))]);
    }

    /// <summary>
    /// What Google should hold for a session week: one repeating event on the week's live days (at the first
    /// day's time), with each day given its own title ("Morning Devotion: theme"), speaker and actual time.
    /// The description of the series lists every day, so guests see the whole week in one invite.
    /// </summary>
    public GoogleEventData BuildWeek(SessionWeek week, IReadOnlyList<CalendarEvent> days, GoogleCalendarInvite invite)
    {
        var zone = ZonedTime.Find(organisation.Value.DefaultTimeZone);
        var ordered = days.OrderBy(d => d.StartAt).ToList();
        var first = ordered[0];
        var startTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(first.StartAt, zone).DateTime);
        var length = first.EndAt - first.StartAt;
        DateOnly LocalDate(CalendarEvent d) => ZonedTime.LocalDate(d.StartAt, zone);
        DateTimeOffset RuleStart(CalendarEvent d) =>
            new(TimeZoneInfo.ConvertTimeToUtc(LocalDate(d).ToDateTime(startTime), zone), TimeSpan.Zero);

        var byDay = string.Join(",", ordered.Select(d => LocalDate(d).DayOfWeek switch
        {
            DayOfWeek.Monday => "MO", DayOfWeek.Tuesday => "TU", DayOfWeek.Wednesday => "WE", DayOfWeek.Thursday => "TH",
            DayOfWeek.Friday => "FR", DayOfWeek.Saturday => "SA", _ => "SU",
        }).Distinct());
        var until = RuleStart(ordered[^1]).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var culture = CultureInfo.GetCultureInfo("en-GB");
        var description = new StringBuilder($"{week.Name} · week of {week.WeekStart.ToString("d MMMM yyyy", culture)}").AppendLine().AppendLine();
        foreach (var day in ordered)
        {
            var local = TimeZoneInfo.ConvertTime(day.StartAt, zone);
            description.Append($"{local.ToString("ddd d MMM, HH:mm", culture)} · {day.Theme ?? "Theme to follow"}");
            description.AppendLine(day.Speaker is { } s ? $" · Sharing: {s}" : string.Empty);
        }
        description.AppendLine();
        if (week.MeetingUrl is { } url)
        {
            description.AppendLine($"Meeting link: {url}").AppendLine();
        }
        description.Append("Sent from DOF Production Manager.");

        string DayDescription(CalendarEvent d) =>
            (d.Speaker is { } s ? $"Sharing: {s}\n\n" : string.Empty)
            + (d.MeetingUrl is { } link ? $"Meeting link: {link}\n\n" : string.Empty)
            + $"Part of {week.Name}, week of {week.WeekStart.ToString("d MMMM yyyy", culture)}.\n\nSent from DOF Production Manager.";

        return new GoogleEventData(invite.GoogleEventId, week.Name, description.ToString(), week.Location ?? week.MeetingUrl,
            new GoogleEventTime(null, RuleStart(first), zone.Id), new GoogleEventTime(null, RuleStart(first) + length, zone.Id),
            [.. invite.Guests.OrderBy(g => g.Email, StringComparer.Ordinal).Select(g => new GoogleAttendee(g.Email, g.DisplayName))],
            [$"RRULE:FREQ=WEEKLY;BYDAY={byDay};UNTIL={until}"],
            [.. ordered.Select(d => new GoogleEventInstance(RuleStart(d), d.Title, DayDescription(d),
                new GoogleEventTime(null, d.StartAt, zone.Id), new GoogleEventTime(null, d.EndAt, zone.Id)))]);
    }

    public static string Hash(GoogleEventData data) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, Json)));

    private static DateOnly EndDate(CalendarEvent e, TimeZoneInfo zone)
    {
        // All-day events end at the start of the following day; Google's end date is exclusive too.
        var last = ZonedTime.LocalDate(e.EndAt.AddTicks(-1), zone);
        return last.AddDays(1);
    }

    private async Task<bool> ProcessAsync(Guid inviteId, GoogleCalendarConnection? connection, DateTimeOffset now, CancellationToken ct)
    {
        var invite = await db.GoogleCalendarInvites.Include(i => i.Guests).SingleAsync(i => i.Id == inviteId, ct);
        bool cancelled;
        DateTimeOffset endsAt;
        GoogleEventData? data;
        if (invite.SessionWeekId is { } weekId)
        {
            var week = await db.SessionWeeks.AsNoTracking().SingleAsync(w => w.Id == weekId, ct);
            var days = await db.CalendarEvents.AsNoTracking()
                .Where(e => e.SessionWeekId == weekId && e.Status != CalendarEventStatus.Cancelled).ToListAsync(ct);
            cancelled = days.Count == 0;
            endsAt = days.Count == 0 ? now : days.Max(d => d.EndAt);
            data = cancelled ? null : BuildWeek(week, days, invite);
        }
        else
        {
            var calendarEvent = await db.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == invite.CalendarEventId, ct);
            cancelled = calendarEvent.Status == CalendarEventStatus.Cancelled;
            endsAt = calendarEvent.EndAt;
            data = Build(calendarEvent, invite);
        }
        var remove = invite.Status == GoogleInviteStatus.Removing || cancelled;
        var hash = data is null ? string.Empty : Hash(data);

        var changed = invite.Status != GoogleInviteStatus.Synced || hash != invite.SyncedHash || remove;
        var checkResponses = !changed && endsAt > now
                             && (invite.ResponsesCheckedAt is not { } checkedAt || checkedAt + ResponseCheckInterval <= now);
        if (!changed && !checkResponses)
        {
            return false;
        }

        if (connection is null)
        {
            invite.Waiting("Google Calendar is not connected. Connect an account in Settings.", now, NotConnectedDelay);
            await db.SaveChangesAsync(ct);
            return false;
        }
        if (invite.ConnectionId != connection.Id)
        {
            if (!await SameAccountAsync(invite.ConnectionId, connection, ct))
            {
                invite.FailPermanently("These invites were sent from a Google account that is no longer connected. Send them again.");
                await db.SaveChangesAsync(ct);
                return false;
            }
            invite.MoveTo(connection.Id);
        }

        try
        {
            var token = protector.Unprotect(connection.EncryptedRefreshToken, GoogleConnectionService.RefreshTokenPurpose);
            if (remove)
            {
                if (invite.CreatedInGoogle)
                {
                    await google.DeleteEventAsync(token, connection.CalendarId, invite.GoogleEventId, ct);
                }
                invite.Removed(now, invite.Status == GoogleInviteStatus.Removing ? null
                    : invite.SessionWeekId is null ? "The event was cancelled." : "Every day of the week was cancelled.");
                logger.LogInformation("Google event for {Target} removed", invite.SessionWeekId ?? invite.CalendarEventId);
            }
            else if (changed)
            {
                var responses = await google.UpsertEventAsync(token, connection.CalendarId, data!, invite.CreatedInGoogle, ct);
                ApplyResponses(invite, responses);
                invite.Synced(hash, now);
                logger.LogInformation("Google event for {Target} sent to {Count} guests",
                    invite.SessionWeekId ?? invite.CalendarEventId, data!.Attendees.Count);
            }
            else
            {
                var responses = await google.GetResponsesAsync(token, connection.CalendarId, invite.GoogleEventId, ct);
                if (responses is null)
                {
                    // Deleted in Google by hand: send it again so the guests' calendars match DOF.
                    invite.SetGuests(invite.Guests.Select(g => (g.Email, g.UserId, g.DisplayName)).ToList(), connection.Id, now);
                }
                else
                {
                    ApplyResponses(invite, responses);
                    invite.ResponsesChecked(now);
                }
            }
            connection.ClearError();
            await db.SaveChangesAsync(ct);
            return changed;
        }
        catch (Exception ex) when (ex is GoogleCalendarException or HttpRequestException or CryptographicException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Google Calendar sync failed for {Target}", invite.SessionWeekId ?? invite.CalendarEventId);
            var message = ex switch
            {
                GoogleCalendarException { AccessRevoked: true } =>
                    "Google refused access to the connected account. Connect it again in Settings.",
                CryptographicException => "The stored Google access could not be read. Connect the account again in Settings.",
                GoogleCalendarException g => g.Message,
                _ => "Google Calendar could not be reached. DOF will try again.",
            };
            if (ex is GoogleCalendarException { AccessRevoked: true } or CryptographicException)
            {
                connection.RecordError(message, now);
            }
            if (changed)
            {
                invite.RecordFailure(message, now);
            }
            else
            {
                invite.ResponsesChecked(now);
            }
            await db.SaveChangesAsync(ct);
            return false;
        }
    }

    /// <summary>The same Google account connected again: its existing events can still be updated.</summary>
    private async Task<bool> SameAccountAsync(Guid previousConnectionId, GoogleCalendarConnection current, CancellationToken ct) =>
        await db.GoogleCalendarConnections.AnyAsync(c => c.Id == previousConnectionId && c.AccountEmail == current.AccountEmail, ct);

    private static void ApplyResponses(GoogleCalendarInvite invite, IReadOnlyList<GoogleAttendeeResponse> responses)
    {
        foreach (var r in responses)
        {
            invite.SetResponse(r.Email, r.ResponseStatus switch
            {
                "accepted" => GuestResponse.Accepted,
                "declined" => GuestResponse.Declined,
                "tentative" => GuestResponse.Tentative,
                _ => GuestResponse.Pending,
            });
        }
    }
}
