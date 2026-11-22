using DOF.Application.Common.Abstractions;
using DOF.Application.Releases;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DOF.Application.Calendar;

/// <summary>
/// Releases at their scheduled time (kept after publication), read from the release itself. Status is
/// the stored status, or "Overdue" when the time has passed without a publication confirmation.
/// Postponed releases without a new time are not on the calendar.
/// </summary>
public sealed class ReleaseCalendarSource(IApplicationDbContext db, ReleaseAccess access, TimeProvider clock) : ICalendarSource
{
    public const string ReleasePriority = "Normal";

    public string SourceType => nameof(CalendarSourceType.Release);

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, ICurrentUser user, CancellationToken ct)
    {
        if (!CalendarEventMapping.Includes(query.EventTypes, CalendarValues.ReleaseItem)
            || !CalendarEventMapping.Includes(query.Priorities, ReleasePriority))
        {
            return [];
        }

        var releases = db.Releases.AsNoTracking().Where(r => r.ScheduledAt >= query.Start && r.ScheduledAt < query.End);
        var wantsOverdue = query.Statuses?.Contains(ReleaseValues.Overdue) == true;
        if (WorkParsing.ParseAll<ReleaseStatus>(query.Statuses?.Where(s => s != ReleaseValues.Overdue).ToList()) is { } statuses)
        {
            releases = releases.Where(r => statuses.Contains(r.Status) || wantsOverdue);
        }
        else if (!query.IncludeCancelled)
        {
            releases = releases.Where(r => r.Status != ReleaseStatus.Cancelled && r.Episode.Show.Status != ShowStatus.Archived);
        }
        if (query.AssignedUserId is { } assignee)
        {
            releases = releases.Where(r => r.AssignedTo == assignee);
        }
        if (query.ProjectId is { } project)
        {
            releases = releases.Where(r => r.ProjectId == project);
        }
        if (query.ShowId is { } show)
        {
            releases = releases.Where(r => r.Episode.ShowId == show);
        }

        var rows = await releases.Include(r => r.Channel)
            .Include(r => r.Episode).ThenInclude(e => e.Show)
            .Include(r => r.Episode).ThenInclude(e => e.Season)
            .ToListAsync(ct);
        var assigneeIds = rows.Select(r => r.AssignedTo).OfType<Guid>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var now = clock.GetUtcNow();

        var items = rows.Select(r =>
        {
            var overdue = r.IsOverdue(now);
            var code = Episode.Code(r.Episode.Season?.SeasonNumber, r.Episode.EpisodeNumber);
            return new CalendarItemResponse($"{SourceType}:{r.Id}",
                $"{r.Channel.Name} · {$"{r.Episode.Show.Name} {code}".TrimEnd()}: {r.Episode.Title}", SourceType, r.Id,
                CalendarValues.ReleaseItem, r.ScheduledAt!.Value, null, false,
                overdue ? ReleaseValues.Overdue : r.Status.ToString(), ReleasePriority, r.Channel.Name,
                r.AssignedTo is { } a && names.TryGetValue(a, out var name) ? [new AssignedUserResponse(a, name, "Publishing")] : [],
                access.CanSchedule && !r.IsClosed);
        });
        // An "Overdue" status filter matches the derived status, alone or alongside stored ones.
        return wantsOverdue
            ? [.. items.Where(i => i.Status == ReleaseValues.Overdue || query.Statuses!.Contains(i.Status))]
            : [.. items];
    }
}
