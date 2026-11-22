using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;

namespace DOF.Application.Calendar;

/// <summary>
/// A request for calendar items overlapping [Start, End). Empty filter collections mean "no filter".
/// Project and show filters only match items that belong to a project or show.
/// </summary>
public sealed record CalendarQuery(
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyCollection<string>? EventTypes = null,
    IReadOnlyCollection<string>? SourceTypes = null,
    IReadOnlyCollection<string>? Statuses = null,
    IReadOnlyCollection<string>? Priorities = null,
    Guid? AssignedUserId = null,
    Guid? ProjectId = null,
    Guid? ShowId = null,
    bool IncludeCancelled = false);

/// <summary>
/// Contributes calendar items from one kind of original record. The calendar is assembled from these
/// sources at query time, so a date is stored once — in the record that owns it.
/// </summary>
public interface ICalendarSource
{
    string SourceType { get; }

    Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, ICurrentUser user, CancellationToken ct);
}
