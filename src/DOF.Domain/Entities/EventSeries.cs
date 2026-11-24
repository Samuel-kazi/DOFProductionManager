using System.Globalization;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.Domain.Entities;

/// <summary>
/// How a repeating event repeats (every day, every N weeks on chosen days, every month) and until when.
/// Each occurrence is an ordinary calendar event linked to the series, so it keeps its own date, can be
/// moved or cancelled on its own, and reminders, invites and PDFs treat it like any other event.
/// </summary>
public sealed class EventSeries : AuditableEntity, IVersioned
{
    public const int MaxOccurrences = 200;
    public const int MaxYears = 2;
    public const int MaxInterval = 12;

    private EventSeries() { }

    public RepeatFrequency Frequency { get; private set; }

    /// <summary>Every 1, 2, … days, weeks or months.</summary>
    public int Interval { get; private set; }

    /// <summary>For weekly series: the days, as a comma-separated list of day names (e.g. "Tuesday,Thursday").</summary>
    public string? Weekdays { get; private set; }

    public DateOnly? Until { get; private set; }
    public int? Count { get; private set; }
    public int Version { get; private set; }

    public static EventSeries Create(RepeatFrequency frequency, int interval, IEnumerable<DayOfWeek>? weekdays, DateOnly? until, int? count,
        DateOnly firstDate)
    {
        if (interval < 1 || interval > MaxInterval)
        {
            throw new DomainException($"Repeat every 1 to {MaxInterval} {Unit(frequency, 2)}.");
        }
        if (until is null == count is null)
        {
            throw new DomainException("Say when the repeating stops: an end date or a number of times.");
        }
        if (until is { } u && (u <= firstDate || u > firstDate.AddYears(MaxYears)))
        {
            throw new DomainException($"The end date must be after the first event and at most {MaxYears} years later.");
        }
        if (count is { } c && (c < 2 || c > MaxOccurrences))
        {
            throw new DomainException($"A repeating event happens 2 to {MaxOccurrences} times.");
        }
        var days = frequency == RepeatFrequency.Weekly
            ? (weekdays?.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList() is { Count: > 0 } chosen ? chosen : [firstDate.DayOfWeek])
            : null;
        return new EventSeries
        {
            Frequency = frequency,
            Interval = interval,
            Weekdays = days is null ? null : string.Join(",", days),
            Until = until,
            Count = count,
        };
    }

    public IReadOnlyList<DayOfWeek> Days =>
        Weekdays is null ? [] : [.. Weekdays.Split(',').Select(Enum.Parse<DayOfWeek>)];

    /// <summary>The dates of every occurrence, starting with <paramref name="firstDate"/> (always included).</summary>
    public IReadOnlyList<DateOnly> Dates(DateOnly firstDate)
    {
        var last = Until ?? firstDate.AddYears(MaxYears);
        var limit = Count ?? MaxOccurrences;
        var dates = new List<DateOnly> { firstDate };
        switch (Frequency)
        {
            case RepeatFrequency.Daily:
                for (var d = firstDate.AddDays(Interval); d <= last && dates.Count < limit; d = d.AddDays(Interval))
                {
                    dates.Add(d);
                }
                break;
            case RepeatFrequency.Weekly:
                var days = Days.ToHashSet();
                var weekStart = firstDate.AddDays(-(((int)firstDate.DayOfWeek + 6) % 7));
                for (var week = weekStart; week <= last && dates.Count < limit; week = week.AddDays(7 * Interval))
                {
                    for (var i = 0; i < 7 && dates.Count < limit; i++)
                    {
                        var d = week.AddDays(i);
                        if (d > firstDate && d <= last && days.Contains(d.DayOfWeek))
                        {
                            dates.Add(d);
                        }
                    }
                }
                break;
            case RepeatFrequency.Monthly:
                // The same day of the month; months without that day (e.g. the 31st) are skipped.
                for (var n = Interval; dates.Count < limit; n += Interval)
                {
                    var month = new DateOnly(firstDate.Year, firstDate.Month, 1).AddMonths(n);
                    if (month > last)
                    {
                        break;
                    }
                    if (firstDate.Day <= DateTime.DaysInMonth(month.Year, month.Month))
                    {
                        var d = new DateOnly(month.Year, month.Month, firstDate.Day);
                        if (d <= last)
                        {
                            dates.Add(d);
                        }
                    }
                }
                break;
        }
        return dates;
    }

    /// <summary>"Every week on Tue and Thu until 30 Jun 2027", "Every 2 weeks on Mon, 10 times", "Every month".</summary>
    public string Describe()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var every = Interval == 1 ? $"Every {Unit(Frequency, 1)}" : $"Every {Interval} {Unit(Frequency, Interval)}";
        if (Frequency == RepeatFrequency.Weekly && Days.Count > 0)
        {
            var names = Days.Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d)).ToList();
            every += " on " + (names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}");
        }
        return every + (Until is { } u ? $" until {u.ToString("d MMM yyyy", culture)}" : Count is { } c ? $", {c} times" : string.Empty);
    }

    private static string Unit(RepeatFrequency frequency, int n) => (frequency, n == 1) switch
    {
        (RepeatFrequency.Daily, true) => "day",
        (RepeatFrequency.Daily, false) => "days",
        (RepeatFrequency.Weekly, true) => "week",
        (RepeatFrequency.Weekly, false) => "weeks",
        (_, true) => "month",
        _ => "months",
    };
}
