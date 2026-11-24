using DOF.Domain.Entities;
using DOF.Domain.Enums;
using DOF.Domain.Exceptions;

namespace DOF.UnitTests;

public class EventSeriesTests
{
    private static readonly DateOnly Tuesday = new(2035, 1, 2);

    [Fact]
    public void Weekly_series_repeat_on_the_chosen_days_until_the_end_date()
    {
        var series = EventSeries.Create(RepeatFrequency.Weekly, 1, [DayOfWeek.Thursday, DayOfWeek.Tuesday], new DateOnly(2035, 1, 16), null, Tuesday);

        Assert.Equal([2, 4, 9, 11, 16], series.Dates(Tuesday).Select(d => d.Day));
        Assert.Equal("Every week on Tue and Thu until 16 Jan 2035", series.Describe());
    }

    [Fact]
    public void Every_two_weeks_daily_and_monthly_series_follow_their_interval()
    {
        Assert.Equal([2, 16, 30], EventSeries.Create(RepeatFrequency.Weekly, 2, null, new DateOnly(2035, 2, 5), null, Tuesday)
            .Dates(Tuesday).Select(d => d.Day));
        Assert.Equal(3, EventSeries.Create(RepeatFrequency.Daily, 1, null, null, 3, Tuesday).Dates(Tuesday).Count);

        // The 31st: months without one are skipped.
        var last = new DateOnly(2035, 1, 31);
        var monthly = EventSeries.Create(RepeatFrequency.Monthly, 1, null, new DateOnly(2035, 6, 30), null, last);
        Assert.Equal([new DateOnly(2035, 1, 31), new(2035, 3, 31), new(2035, 5, 31)], monthly.Dates(last));
        Assert.Equal("Every month until 30 Jun 2035", monthly.Describe());
    }

    [Fact]
    public void A_series_needs_one_sensible_end()
    {
        Assert.Throws<DomainException>(() => EventSeries.Create(RepeatFrequency.Daily, 1, null, null, null, Tuesday));
        Assert.Throws<DomainException>(() => EventSeries.Create(RepeatFrequency.Daily, 1, null, Tuesday, null, Tuesday));
        Assert.Throws<DomainException>(() => EventSeries.Create(RepeatFrequency.Daily, 1, null, Tuesday.AddYears(3), null, Tuesday));
        Assert.Throws<DomainException>(() => EventSeries.Create(RepeatFrequency.Daily, 0, null, null, 5, Tuesday));
        Assert.Equal(EventSeries.MaxOccurrences,
            EventSeries.Create(RepeatFrequency.Daily, 1, null, Tuesday.AddYears(2), null, Tuesday).Dates(Tuesday).Count);
    }
}
