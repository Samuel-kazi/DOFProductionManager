using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Enums;
using DOF.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Calendar;

/// <summary>Standalone events from <c>calendar_events</c>.</summary>
public sealed class StandaloneEventCalendarSource(IApplicationDbContext db) : ICalendarSource
{
    public string SourceType => nameof(CalendarSourceType.CalendarEvent);

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(
        CalendarQuery query, ICurrentUser user, CancellationToken ct)
    {
        // Standalone events never belong to a project or show.
        if (query.ProjectId is not null || query.ShowId is not null)
        {
            return [];
        }

        var events = db.CalendarEvents.AsNoTracking()
            .Where(e => e.StartAt < query.End && e.EndAt > query.Start);

        var types = ParseAll<CalendarEventType>(query.EventTypes);
        if (types is not null)
        {
            events = events.Where(e => types.Contains(e.EventType));
        }

        var statuses = ParseAll<CalendarEventStatus>(query.Statuses);
        if (statuses is not null)
        {
            events = events.Where(e => statuses.Contains(e.Status));
        }
        else if (!query.IncludeCancelled)
        {
            events = events.Where(e => e.Status != CalendarEventStatus.Cancelled);
        }

        var priorities = ParseAll<Priority>(query.Priorities);
        if (priorities is not null)
        {
            events = events.Where(e => priorities.Contains(e.Priority));
        }

        if (query.AssignedUserId is { } assignee)
        {
            events = events.Where(e => e.Assignments.Any(a => a.UserId == assignee));
        }

        var canEdit = user.HasPermission(Permissions.CalendarManage);
        var rows = await events
            .OrderBy(e => e.StartAt)
            .Select(e => new
            {
                e.Id, e.Title, e.EventType, e.StartAt, e.EndAt, e.IsAllDay, e.Status, e.Priority, e.Location, e.Speaker, e.SessionWeekId, e.SeriesId,
                SessionName = db.SessionWeeks.Where(w => w.Id == e.SessionWeekId).Select(w => w.Name).FirstOrDefault(),
                Assigned = e.Assignments
                    .Select(a => new { a.UserId, a.User.FullName, a.AssignmentType })
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(e => new CalendarItemResponse(
                $"{SourceType}:{e.Id}",
                e.Title,
                SourceType,
                e.Id,
                e.EventType.ToString(),
                e.StartAt,
                e.EndAt,
                e.IsAllDay,
                e.Status.ToString(),
                e.Priority.ToString(),
                e.Location,
                e.Assigned
                    .OrderBy(a => a.AssignmentType).ThenBy(a => a.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(a => new AssignedUserResponse(a.UserId, a.FullName, a.AssignmentType.ToString())).ToList(),
                canEdit,
                e.Speaker,
                e.SessionWeekId,
                e.SessionName,
                e.SeriesId != null))
            .ToList();
    }

    /// <summary>Null when there is no filter. Values that are not event types of this source match nothing.</summary>
    private static List<T>? ParseAll<T>(IReadOnlyCollection<string>? values) where T : struct, Enum =>
        values is null || values.Count == 0
            ? null
            : values.Select(v => Enum.TryParse<T>(v, out var parsed) ? parsed : (T?)null)
                .OfType<T>()
                .ToList();
}
