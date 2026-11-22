using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DOF.Application.Calendar;

/// <summary>Create, read, update and cancel standalone calendar events.</summary>
public sealed class CalendarEventService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<OrganisationOptions> organisation,
    ILogger<CalendarEventService> logger)
{
    public async Task<PagedResponse<CalendarEventResponse>> ListAsync(
        int? page, int? pageSize, string? search, DateTimeOffset? from, DateTimeOffset? to, bool includeCancelled,
        CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.CalendarEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(e => e.Title.ToLower().Contains(term));
        }
        if (from is not null)
        {
            query = query.Where(e => e.EndAt > from);
        }
        if (to is not null)
        {
            query = query.Where(e => e.StartAt < to);
        }
        if (!includeCancelled)
        {
            query = query.Where(e => e.Status != CalendarEventStatus.Cancelled);
        }

        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(e => e.StartAt).ThenBy(e => e.Id)
            .Skip((p - 1) * size).Take(size).Select(e => e.Id).ToListAsync(ct);
        var items = await LoadResponsesAsync(ids, ct);
        return new PagedResponse<CalendarEventResponse>(ids.Select(id => items[id]).ToList(), p, size, total);
    }

    public async Task<CalendarEventResponse> GetAsync(Guid id, CancellationToken ct) =>
        (await LoadResponsesAsync([id], ct)).GetValueOrDefault(id) ?? throw new NotFoundException("Event", id);

    public async Task<CalendarEventResponse> CreateAsync(CreateCalendarEventRequest request, CancellationToken ct)
    {
        var (start, end) = Times(request.StartAt, request.EndAt, request.IsAllDay);
        var calendarEvent = CalendarEvent.Create(
            request.Title, request.Description, CalendarEventMapping.Parse<CalendarEventType>(request.EventType, "event type"),
            start, end, request.IsAllDay, request.Location, request.MeetingUrl,
            CalendarEventMapping.Parse<Priority>(request.Priority, "priority"));

        await ApplyAssignmentsAsync(calendarEvent, request.Assignments, ct);
        db.CalendarEvents.Add(calendarEvent);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Calendar event {EventId} created by {UserId}", calendarEvent.Id, currentUser.UserId);
        return await GetAsync(calendarEvent.Id, ct);
    }

    public async Task<CalendarEventResponse> UpdateAsync(Guid id, UpdateCalendarEventRequest request, CancellationToken ct)
    {
        var calendarEvent = await LoadForUpdateAsync(id, request.Version, ct);
        var (start, end) = Times(request.StartAt, request.EndAt, request.IsAllDay);

        calendarEvent.Update(
            request.Title, request.Description, CalendarEventMapping.Parse<CalendarEventType>(request.EventType, "event type"),
            start, end, request.IsAllDay, request.Location, request.MeetingUrl,
            CalendarEventMapping.Parse<Priority>(request.Priority, "priority"),
            CalendarEventMapping.Parse<CalendarEventStatus>(request.Status, "status"));
        await ApplyAssignmentsAsync(calendarEvent, request.Assignments, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Calendar event {EventId} updated by {UserId}", id, currentUser.UserId);
        return await GetAsync(id, ct);
    }

    /// <summary>Marks the event cancelled. It stays in the database (and in history) but leaves the calendar.</summary>
    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var calendarEvent = await db.CalendarEvents.SingleOrDefaultAsync(e => e.Id == id, ct)
                            ?? throw new NotFoundException("Event", id);
        if (calendarEvent.Status == CalendarEventStatus.Cancelled)
        {
            return;
        }

        calendarEvent.Cancel();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Calendar event {EventId} cancelled by {UserId}", id, currentUser.UserId);
    }

    private async Task<CalendarEvent> LoadForUpdateAsync(Guid id, int version, CancellationToken ct)
    {
        var calendarEvent = await db.CalendarEvents.Include(e => e.Assignments).SingleOrDefaultAsync(e => e.Id == id, ct)
                            ?? throw new NotFoundException("Event", id);
        if (calendarEvent.Version != version)
        {
            throw new ConflictException("This event was modified by someone else. Reload the latest version and try again.");
        }
        db.Entry(calendarEvent).Property(e => e.Version).OriginalValue = version;
        return calendarEvent;
    }

    private (DateTimeOffset Start, DateTimeOffset End) Times(DateTimeOffset start, DateTimeOffset end, bool isAllDay) =>
        isAllDay ? ZonedTime.SnapToWholeDays(start, end, ZonedTime.Find(organisation.Value.DefaultTimeZone)) : (start, end);

    private async Task ApplyAssignmentsAsync(
        CalendarEvent calendarEvent, IReadOnlyList<CalendarAssignmentRequest>? requests, CancellationToken ct)
    {
        var assignments = CalendarEventMapping.Assignments(requests).ToList();
        var userIds = assignments.Select(a => a.Item1).Distinct().ToList();
        var activeUsers = await db.Users.CountAsync(u => userIds.Contains(u.Id) && u.IsActive, ct);
        if (activeUsers != userIds.Count)
        {
            throw new DomainException("Events can only be assigned to existing, active users.");
        }

        calendarEvent.SetAssignments(assignments);
    }

    private async Task<Dictionary<Guid, CalendarEventResponse>> LoadResponsesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var canEdit = currentUser.HasPermission(Permissions.CalendarManage);
        var rows = await db.CalendarEvents.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new
            {
                Event = e,
                CreatedByName = db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
                Assigned = e.Assignments
                    .Select(a => new { a.UserId, a.User.FullName, a.AssignmentType })
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Event.Id, r => new CalendarEventResponse(
            r.Event.Id, r.Event.Title, r.Event.Description, r.Event.EventType.ToString(), r.Event.StartAt, r.Event.EndAt,
            r.Event.IsAllDay, r.Event.Location, r.Event.MeetingUrl, r.Event.Priority.ToString(), r.Event.Status.ToString(),
            r.Assigned
                    .OrderBy(a => a.AssignmentType).ThenBy(a => a.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(a => new AssignedUserResponse(a.UserId, a.FullName, a.AssignmentType.ToString())).ToList(),
            r.Event.CreatedBy, r.CreatedByName, r.Event.CreatedAt, r.Event.UpdatedAt, r.Event.Version, canEdit));
    }
}
