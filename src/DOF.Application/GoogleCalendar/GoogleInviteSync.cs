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
                        && db.CalendarEvents.Any(e => e.Id == i.CalendarEventId && e.EndAt >= recentlyEnded))
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
        var calendarEvent = await db.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == invite.CalendarEventId, ct);
        var remove = invite.Status == GoogleInviteStatus.Removing || calendarEvent.Status == CalendarEventStatus.Cancelled;
        var data = Build(calendarEvent, invite);
        var hash = Hash(data);

        var changed = invite.Status != GoogleInviteStatus.Synced || hash != invite.SyncedHash || remove;
        var checkResponses = !changed && calendarEvent.EndAt > now
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
                invite.Removed(now, invite.Status == GoogleInviteStatus.Removing ? null : "The event was cancelled.");
                logger.LogInformation("Google event for calendar event {EventId} removed", invite.CalendarEventId);
            }
            else if (changed)
            {
                var responses = await google.UpsertEventAsync(token, connection.CalendarId, data, invite.CreatedInGoogle, ct);
                ApplyResponses(invite, responses);
                invite.Synced(hash, now);
                logger.LogInformation("Google event for calendar event {EventId} sent to {Count} guests",
                    invite.CalendarEventId, data.Attendees.Count);
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
            logger.LogWarning(ex, "Google Calendar sync failed for calendar event {EventId}", invite.CalendarEventId);
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
