namespace DOF.Application.Common;

/// <summary>Conversions between UTC instants and calendar dates in a given timezone.</summary>
public static class ZonedTime
{
    public static TimeZoneInfo Find(string timeZoneId) => TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    /// <summary>The UTC instant at which <paramref name="date"/> begins in <paramref name="zone"/>.</summary>
    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // In a daylight-saving gap midnight may not exist; the day then starts at the first valid time.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    public static bool IsLocalMidnight(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).TimeOfDay == TimeSpan.Zero;

    /// <summary>
    /// Snaps an all-day span to whole days in <paramref name="zone"/>: from midnight of the first day to the
    /// midnight after the last day. <paramref name="end"/> may be any moment on the last day; an end that is
    /// already a midnight is treated as exclusive, so saving an event again does not lengthen it.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) SnapToWholeDays(
        DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var firstDay = LocalDate(start, zone);
        var lastDay = LocalDate(end, zone);
        if (end > start && IsLocalMidnight(end, zone))
        {
            lastDay = lastDay.AddDays(-1);
        }
        if (lastDay < firstDay)
        {
            throw new Domain.Exceptions.DomainException("The event must end after it starts.");
        }

        return (StartOfDay(firstDay, zone), StartOfDay(lastDay.AddDays(1), zone));
    }

    /// <summary>Monday of the week containing <paramref name="date"/> (weeks start on Monday).</summary>
    public static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
