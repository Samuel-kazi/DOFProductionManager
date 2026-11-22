using DOF.Application.Calendar;
using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Responses;
using DOF.Domain.Exceptions;

namespace DOF.Application.Dashboard;

/// <summary>
/// Dashboards built from the aggregated calendar. Every entry is a calendar item that links to its
/// original record; there are no decorative statistics.
/// </summary>
public sealed class DashboardService(CalendarService calendar, ICurrentUser currentUser)
{
    public const int DefaultDays = 7;
    public const int MaxDays = 60;

    /// <summary>Today's schedule and the next week's items assigned to the signed-in user.</summary>
    public async Task<PersonalDashboardResponse> GetPersonalAsync(string? timeZone, CancellationToken ct)
    {
        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var today = calendar.Today(zone);
        var me = currentUser.RequiredUserId;

        var todayStart = ZonedTime.StartOfDay(today, zone);
        var tomorrowStart = ZonedTime.StartOfDay(today.AddDays(1), zone);
        var todaySchedule = await calendar.GetItemsAsync(new CalendarQuery(todayStart, tomorrowStart, AssignedUserId: me), ct);
        var upcoming = await calendar.GetItemsAsync(new CalendarQuery(
            tomorrowStart, ZonedTime.StartOfDay(today.AddDays(1 + DefaultDays), zone), AssignedUserId: me), ct);

        return new PersonalDashboardResponse(today, zone.Id, todaySchedule, upcoming);
    }

    /// <summary>
    /// Organisation-wide upcoming activities, plus the ones nobody is responsible for yet (every activity
    /// should have a responsible person).
    /// </summary>
    public async Task<ManagementDashboardResponse> GetManagementAsync(
        DateOnly? start, DateOnly? end, string? timeZone, CancellationToken ct)
    {
        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var (from, to) = Range(start, end, zone);
        var items = await calendar.GetItemsAsync(new CalendarQuery(from, to), ct);
        return new ManagementDashboardResponse(from, to, zone.Id, items, [.. items.Where(i => i.AssignedUsers.Count == 0)]);
    }

    /// <summary>Upcoming items for the organisation, or only the signed-in user's when <paramref name="mine"/> is set.</summary>
    public async Task<CalendarRangeResponse> GetUpcomingAsync(int? days, bool mine, string? timeZone, CancellationToken ct)
    {
        var count = days ?? DefaultDays;
        if (count is < 1 or > MaxDays)
        {
            throw new DomainException($"Upcoming covers between 1 and {MaxDays} days.");
        }

        var zone = await calendar.ResolveZoneAsync(timeZone, ct);
        var today = calendar.Today(zone);
        var from = ZonedTime.StartOfDay(today, zone);
        var to = ZonedTime.StartOfDay(today.AddDays(count), zone);
        var items = await calendar.GetItemsAsync(
            new CalendarQuery(from, to, AssignedUserId: mine ? currentUser.RequiredUserId : null), ct);
        return new CalendarRangeResponse(from, to, zone.Id, items);
    }

    private (DateTimeOffset From, DateTimeOffset To) Range(DateOnly? start, DateOnly? end, TimeZoneInfo zone)
    {
        var first = start ?? calendar.Today(zone);
        var last = end ?? first.AddDays(DefaultDays - 1);
        if (last < first)
        {
            throw new DomainException("The end date must not be before the start date.");
        }
        if (last.DayNumber - first.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"Dashboards cover at most {MaxDays} days.");
        }

        return (ZonedTime.StartOfDay(first, zone), ZonedTime.StartOfDay(last.AddDays(1), zone));
    }
}
