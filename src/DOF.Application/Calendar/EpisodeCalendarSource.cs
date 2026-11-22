using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Shows;
using DOF.Application.Work;
using DOF.Contracts.Common;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using DOF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Calendar;

/// <summary>
/// Episodes contribute a timed recording session and all-day editing, review and approval deadlines
/// (organisation timezone). Deadlines the episode has already passed through are not shown. Each item
/// opens the episode; item ids are <c>Episode:{id}:{eventType}</c>.
/// </summary>
public sealed class EpisodeCalendarSource(
    IApplicationDbContext db, ShowAccess access, IOptions<OrganisationOptions> organisation) : ICalendarSource
{
    /// <summary>Episodes have no priority of their own; they appear as Normal.</summary>
    public const string EpisodePriority = "Normal";

    public string SourceType => nameof(CalendarSourceType.Episode);

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, ICurrentUser user, CancellationToken ct)
    {
        var wantedTypes = CalendarValues.EpisodeItems.Where(t => CalendarEventMapping.Includes(query.EventTypes, t)).ToHashSet();
        if (query.ProjectId is not null || wantedTypes.Count == 0
            || !CalendarEventMapping.Includes(query.Priorities, EpisodePriority))
        {
            return [];
        }

        var zone = ZonedTime.Find(organisation.Value.DefaultTimeZone);
        var firstDay = ZonedTime.LocalDate(query.Start, zone);
        var lastDay = ZonedTime.LocalDate(query.End.AddTicks(-1), zone);

        var episodes = db.Episodes.AsNoTracking().Where(e =>
            (e.RecordingStart < query.End && e.RecordingEnd > query.Start)
            || (e.EditingDueDate >= firstDay && e.EditingDueDate <= lastDay)
            || (e.ReviewDueDate >= firstDay && e.ReviewDueDate <= lastDay)
            || (e.ApprovalDueDate >= firstDay && e.ApprovalDueDate <= lastDay));

        if (WorkParsing.ParseAll<EpisodeStatus>(query.Statuses) is { } statuses)
        {
            episodes = episodes.Where(e => statuses.Contains(e.Status));
        }
        else if (!query.IncludeCancelled)
        {
            episodes = episodes.Where(e => e.Status != EpisodeStatus.Cancelled && e.Show.Status != ShowStatus.Archived);
        }
        if (query.ShowId is { } show)
        {
            episodes = episodes.Where(e => e.ShowId == show);
        }
        if (query.AssignedUserId is { } assignee)
        {
            episodes = episodes.Where(e => e.ProducerId == assignee || e.Assignments.Any(a => a.UserId == assignee));
        }

        var rows = await episodes
            .Include(e => e.Show).Include(e => e.Season)
            .Include(e => e.Assignments).ThenInclude(a => a.User)
            .ToListAsync(ct);
        var producerIds = rows.Select(e => e.ProducerId).OfType<Guid>().Distinct().ToList();
        var producers = await db.Users.AsNoTracking().Where(u => producerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = new List<CalendarItemResponse>();
        foreach (var e in rows)
        {
            var name = $"{e.Show.Name} {ShowReaders.Code(e)}".TrimEnd() + $": {e.Title}";
            var canEdit = access.CanEditEpisode(e.Show.ProducerId, e.ProducerId);

            if (wantedTypes.Contains(CalendarValues.RecordingItem) && e.RecordingStart is { } start && e.RecordingEnd is { } end
                && start < query.End && end > query.Start)
            {
                items.Add(Item(e, CalendarValues.RecordingItem, $"Recording · {name}", start, end, false,
                    People(e, producers, recording: true, null), canEdit));
            }

            foreach (var (label, date) in e.PendingDeadlines())
            {
                var type = label switch
                {
                    "Editing" => CalendarValues.EditingDeadlineItem,
                    "Review" => CalendarValues.ReviewDeadlineItem,
                    _ => CalendarValues.ApprovalDeadlineItem,
                };
                if (!wantedTypes.Contains(type) || date < firstDay || date > lastDay)
                {
                    continue;
                }
                var responsible = label switch
                {
                    "Editing" => EpisodeResponsibility.Editor,
                    "Review" => EpisodeResponsibility.Reviewer,
                    _ => (EpisodeResponsibility?)null,
                };
                items.Add(Item(e, type, $"{label} due · {name}", ZonedTime.StartOfDay(date, zone),
                    ZonedTime.StartOfDay(date.AddDays(1), zone), true, People(e, producers, recording: false, responsible), canEdit));
            }
        }

        // The assignee filter matches the people on each item, not just anyone on the episode.
        return query.AssignedUserId is { } who
            ? [.. items.Where(i => i.AssignedUsers.Any(a => a.UserId == who))]
            : items;
    }

    private CalendarItemResponse Item(Episode e, string type, string title, DateTimeOffset start, DateTimeOffset end, bool allDay,
        IReadOnlyList<AssignedUserResponse> people, bool canEdit) =>
        new($"{SourceType}:{e.Id}:{type}", title, SourceType, e.Id, type, start, end, allDay, e.Status.ToString(),
            EpisodePriority, type == CalendarValues.RecordingItem ? e.RecordingLocation : null, people, canEdit);

    /// <summary>The producer, plus everyone on the crew for a recording or the responsible people for a deadline.</summary>
    private static List<AssignedUserResponse> People(Episode e, IReadOnlyDictionary<Guid, string> producers,
        bool recording, EpisodeResponsibility? deadlineOwner)
    {
        var people = new List<AssignedUserResponse>();
        if (e.ProducerId is { } p && producers.TryGetValue(p, out var producerName))
        {
            people.Add(new AssignedUserResponse(p, producerName, nameof(EpisodeResponsibility.Producer)));
        }

        foreach (var a in e.Assignments.OrderBy(a => a.Responsibility).ThenBy(a => a.User.FullName))
        {
            var relevant = recording || a.Responsibility == deadlineOwner;
            if (relevant && people.All(x => x.UserId != a.UserId))
            {
                people.Add(new AssignedUserResponse(a.UserId, a.User.FullName, a.Responsibility.ToString()));
            }
        }
        return people;
    }
}
