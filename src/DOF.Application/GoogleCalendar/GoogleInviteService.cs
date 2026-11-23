using DOF.Application.Activity;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.GoogleCalendar;

/// <summary>
/// Google Calendar invites for standalone events (calendar.manage, checked by the controller). Sending
/// saves the guest list and pushes it to Google straight away; later changes to the event reach the
/// guests through <see cref="GoogleInviteSync"/>.
/// </summary>
public sealed class GoogleInviteService(
    IApplicationDbContext db,
    GoogleConnectionService connections,
    GoogleInviteSync sync,
    ActivityRecorder activity,
    TimeProvider clock,
    ILogger<GoogleInviteService> logger)
{
    public const int MaxGuests = 100;

    /// <summary>The event's invites, or null when none were ever sent.</summary>
    public async Task<GoogleInviteResponse?> GetAsync(Guid eventId, CancellationToken ct)
    {
        if (!await db.CalendarEvents.AnyAsync(e => e.Id == eventId, ct))
        {
            throw new NotFoundException("Event", eventId);
        }

        var row = await db.GoogleCalendarInvites.AsNoTracking()
            .Where(i => i.CalendarEventId == eventId)
            .Select(i => new
            {
                Invite = i,
                Guests = i.Guests.ToList(),
                Account = db.GoogleCalendarConnections.Where(c => c.Id == i.ConnectionId).Select(c => c.AccountEmail).FirstOrDefault(),
                SentBy = db.Users.Where(u => u.Id == i.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var userIds = row.Guests.Select(g => g.UserId).OfType<Guid>().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var guests = row.Guests
            .Select(g => new GoogleInviteGuestResponse(g.Email, g.UserId,
                g.UserId is { } id && names.TryGetValue(id, out var name) ? name : g.DisplayName, g.Response.ToString()))
            .OrderBy(g => g.DisplayName ?? g.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new GoogleInviteResponse(eventId, row.Invite.Status.ToString(), row.Account, guests, row.Invite.LastSyncedAt,
            row.Invite.LastError, row.Invite.CreatedAt, row.SentBy);
    }

    public async Task<GoogleInviteResponse> SendAsync(Guid eventId, SendGoogleInvitesRequest request, CancellationToken ct)
    {
        var calendarEvent = await db.CalendarEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct)
                            ?? throw new NotFoundException("Event", eventId);
        var now = clock.GetUtcNow();
        if (calendarEvent.Status == CalendarEventStatus.Cancelled)
        {
            throw new DomainException("This event is cancelled. Reinstate it before sending invites.");
        }
        if (calendarEvent.EndAt <= now)
        {
            throw new DomainException("This event has already ended.");
        }
        var connection = await connections.ActiveAsync(ct)
                         ?? throw new ConflictException("Connect a Google account in Settings before sending invites.");

        var userIds = (request.UserIds ?? []).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName }).ToListAsync(ct);
        if (users.Count != userIds.Count)
        {
            throw new DomainException("Invites can only be sent to existing, active team members.");
        }
        var guests = users.Select(u => (u.Email, (Guid?)u.Id, (string?)u.FullName))
            .Concat((request.Emails ?? []).Select(e => (e.Trim(), (Guid?)null, (string?)null)))
            .Where(g => !string.Equals(g.Item1, connection.AccountEmail, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (guests.Select(g => g.Item1.ToLowerInvariant()).Distinct().Count() > MaxGuests)
        {
            throw new DomainException($"An event can have at most {MaxGuests} guests.");
        }

        var invite = await db.GoogleCalendarInvites.Include(i => i.Guests).SingleOrDefaultAsync(i => i.CalendarEventId == eventId, ct);
        var before = invite?.Status == GoogleInviteStatus.Removed ? [] : invite?.Guests.Select(g => g.Email).ToList() ?? [];
        if (invite is null)
        {
            invite = new GoogleCalendarInvite(eventId, connection.Id, now);
            db.GoogleCalendarInvites.Add(invite);
        }
        else if (invite.ConnectionId != connection.Id
                 && await db.GoogleCalendarConnections.AnyAsync(c => c.Id == invite.ConnectionId && c.AccountEmail == connection.AccountEmail, ct))
        {
            // The same Google account was connected again: keep updating the event it already holds.
            invite.MoveTo(connection.Id);
        }
        invite.SetGuests(guests, connection.Id, now);

        var after = invite.Guests.Select(g => g.Email).ToList();
        activity.Record(ActivityEntities.CalendarEvent, eventId, ActivityActions.InvitesSent,
            before.Count == 0 ? null : new { Guests = before.Order().ToList() },
            new { Guests = after.Order().ToList(), Account = connection.AccountEmail });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Google invites for event {EventId} queued for {Count} guests", eventId, after.Count);

        await sync.SyncOneAsync(invite.Id, now, ct);
        return (await GetAsync(eventId, ct))!;
    }

    /// <summary>Takes the event off every guest's Google Calendar (Google tells them it was cancelled).</summary>
    public async Task<GoogleInviteResponse> RemoveAsync(Guid eventId, CancellationToken ct)
    {
        var invite = await db.GoogleCalendarInvites.SingleOrDefaultAsync(i => i.CalendarEventId == eventId, ct)
                     ?? throw new NotFoundException("Google Calendar invite for event", eventId);
        var now = clock.GetUtcNow();
        if (invite.Status != GoogleInviteStatus.Removed)
        {
            invite.RequestRemoval(now);
            activity.Record(ActivityEntities.CalendarEvent, eventId, ActivityActions.InvitesRemoved);
            await db.SaveChangesAsync(ct);
            await sync.SyncOneAsync(invite.Id, now, ct);
        }
        return (await GetAsync(eventId, ct))!;
    }
}

public sealed class SendGoogleInvitesRequestValidator : AbstractValidator<SendGoogleInvitesRequest>
{
    public SendGoogleInvitesRequestValidator()
    {
        RuleFor(r => (r.UserIds ?? Array.Empty<Guid>()).Count + (r.Emails ?? Array.Empty<string>()).Count)
            .GreaterThan(0).WithMessage("Choose at least one person to invite.").OverridePropertyName("Guests");
        RuleFor(r => r.UserIds ?? Array.Empty<Guid>()).Must(ids => ids.Count <= GoogleInviteService.MaxGuests)
            .WithMessage($"An event can have at most {GoogleInviteService.MaxGuests} guests.").OverridePropertyName("UserIds");
        RuleFor(r => r.Emails ?? Array.Empty<string>()).Must(e => e.Count <= GoogleInviteService.MaxGuests)
            .WithMessage($"An event can have at most {GoogleInviteService.MaxGuests} guests.").OverridePropertyName("Emails");
        RuleForEach(r => r.Emails ?? Array.Empty<string>()).NotEmpty().MaximumLength(320).EmailAddress()
            .WithMessage("'{PropertyValue}' is not a valid e-mail address.").OverridePropertyName("Emails");
    }
}

public sealed class StartGoogleAuthorizationRequestValidator : AbstractValidator<StartGoogleAuthorizationRequest>
{
    public StartGoogleAuthorizationRequestValidator() =>
        RuleFor(r => r.RedirectUri)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback
                       && uri.Host is "127.0.0.1" or "localhost" && !uri.IsDefaultPort && uri.Query.Length == 0)
            .WithMessage("The redirect address must be a loopback address such as http://127.0.0.1:53682/.");
}

public sealed class CompleteGoogleAuthorizationRequestValidator : AbstractValidator<CompleteGoogleAuthorizationRequest>
{
    public CompleteGoogleAuthorizationRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(2048);
        RuleFor(r => r.State).NotEmpty().MaximumLength(4096);
    }
}
