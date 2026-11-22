using DOF.Application.Common;
using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Responses;
using DOF.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOF.Application.Calendar;

/// <summary>Assembles the organisational calendar from every registered <see cref="ICalendarSource"/>.</summary>
public sealed class CalendarService(
    IEnumerable<ICalendarSource> sources,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<OrganisationOptions> organisation,
    TimeProvider clock)
{
    public const int MaxRangeDays = 190;
    public const int MaxAgendaDays = 90;

    public async Task<IReadOnlyList<CalendarItemResponse>> GetItemsAsync(CalendarQuery query, CancellationToken ct)
    {
        if (query.End <= query.Start)
        {
            throw new DomainException("The calendar range must end after it starts.");
        }

        if (query.End - query.Start > TimeSpan.FromDays(MaxRangeDays))
        {
            throw new DomainException($"The calendar range cannot exceed {MaxRangeDays} days.");
        }

        var items = new List<CalendarItemResponse>();
        foreach (var source in sources)
        {
            if (CalendarEventMapping.Includes(query.SourceTypes, source.SourceType))
            {
                items.AddRange(await source.GetItemsAsync(query, currentUser, ct));
            }
        }

        return [.. items.OrderBy(i => i.StartAt).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<CalendarRangeResponse> GetDayAsync(DateOnly? date, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var zone = await ResolveZoneAsync(timeZone, ct);
        var day = date ?? Today(zone);
        return await RangeAsync(day, day.AddDays(1), zone, filters, ct);
    }

    public async Task<CalendarRangeResponse> GetWeekAsync(DateOnly? date, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var zone = await ResolveZoneAsync(timeZone, ct);
        var monday = ZonedTime.StartOfWeek(date ?? Today(zone));
        return await RangeAsync(monday, monday.AddDays(7), zone, filters, ct);
    }

    public async Task<CalendarRangeResponse> GetMonthAsync(int? year, int? month, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var zone = await ResolveZoneAsync(timeZone, ct);
        var today = Today(zone);
        var first = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        return await RangeAsync(first, first.AddMonths(1), zone, filters, ct);
    }

    public async Task<CalendarRangeResponse> GetAgendaAsync(DateOnly? start, int? days, string? timeZone, CalendarQuery filters, CancellationToken ct)
    {
        var zone = await ResolveZoneAsync(timeZone, ct);
        var count = days ?? 14;
        if (count is < 1 or > MaxAgendaDays)
        {
            throw new DomainException($"The agenda covers between 1 and {MaxAgendaDays} days.");
        }

        var first = start ?? Today(zone);
        return await RangeAsync(first, first.AddDays(count), zone, filters, ct);
    }

    /// <summary>The signed-in user's timezone (or an explicitly requested one, or the organisation default).</summary>
    public async Task<TimeZoneInfo> ResolveZoneAsync(string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return TimeZoneInfo.TryFindSystemTimeZoneById(requested, out var explicitZone)
                ? explicitZone
                : throw new DomainException($"'{requested}' is not a recognised IANA timezone.");
        }

        var userZone = currentUser.UserId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.TimeZone).SingleOrDefaultAsync(ct)
            : null;
        return ZonedTime.Find(userZone ?? organisation.Value.DefaultTimeZone);
    }

    public DateOnly Today(TimeZoneInfo zone) => ZonedTime.LocalDate(clock.GetUtcNow(), zone);

    private async Task<CalendarRangeResponse> RangeAsync(
        DateOnly firstDay, DateOnly endDayExclusive, TimeZoneInfo zone, CalendarQuery filters, CancellationToken ct)
    {
        var start = ZonedTime.StartOfDay(firstDay, zone);
        var end = ZonedTime.StartOfDay(endDayExclusive, zone);
        var items = await GetItemsAsync(filters with { Start = start, End = end }, ct);
        return new CalendarRangeResponse(start, end, zone.Id, items);
    }
}
