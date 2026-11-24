using System.Globalization;

namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>One day's part of a bar: clicking it opens that day's item (none on a gap day of a week).</summary>
public sealed record BarSegment(CalendarItemViewModel? Item)
{
    public bool HasItem => Item is not null;
}

/// <summary>
/// A continuous bar across days of one week row: an event lasting several days, or the days of a planned
/// session week (Morning Devotion Monday–Friday) drawn as one bar instead of a highlight per day.
/// </summary>
public sealed class CalendarBar
{
    public const double Height = 20;
    public const double Gap = 2;

    public CalendarBar(int column, int span, string title, string? timeText, string categoryBrushKey, string toolTip,
        IReadOnlyList<BarSegment> segments, bool continuesBefore, bool continuesAfter, bool isCancelled)
    {
        Column = column;
        Span = span;
        Title = title;
        TimeText = timeText;
        CategoryBrushKey = categoryBrushKey;
        ToolTip = toolTip;
        Segments = segments;
        ContinuesBefore = continuesBefore;
        ContinuesAfter = continuesAfter;
        IsCancelled = isCancelled;
    }

    public int Column { get; }
    public int Span { get; }
    public int Lane { get; internal set; }
    public string Title { get; }
    public string? TimeText { get; }
    public bool HasTime => TimeText is not null;
    public string CategoryBrushKey { get; }
    public string ToolTip { get; }
    public IReadOnlyList<BarSegment> Segments { get; }
    public bool ContinuesBefore { get; }
    public bool ContinuesAfter { get; }
    public bool IsCancelled { get; }

    /// <summary>Square ends where the bar carries on into the previous or next week row.</summary>
    public Avalonia.CornerRadius Corners => new(ContinuesBefore ? 0 : 5, ContinuesAfter ? 0 : 5, ContinuesAfter ? 0 : 5, ContinuesBefore ? 0 : 5);

    /// <summary>Places the bar in its lane, below the day numbers (<paramref name="top"/>).</summary>
    public Avalonia.Thickness MarginFor(double top) => new(2, top + Lane * (Height + Gap), 2, 0);
    public Avalonia.Thickness Margin { get; internal set; }

    public string Key { get; internal set; } = string.Empty;
}

public static class CalendarBars
{
    /// <summary>
    /// Finds the bars of one row of <paramref name="days"/> days starting <paramref name="rowStart"/>: items that
    /// last more than one day, and (when <paramref name="joinSessionWeeks"/>) two or more days of the same
    /// session week. Returns the bars (with lanes) and the items they replace, which the day cells leave out.
    /// </summary>
    public static (IReadOnlyList<CalendarBar> Bars, IReadOnlySet<CalendarItemViewModel> Covered) Build(
        IReadOnlyList<CalendarItemViewModel> items, DateOnly rowStart, int days, bool joinSessionWeeks, bool onlyDayLong, double top)
    {
        var rowDays = Enumerable.Range(0, days).Select(rowStart.AddDays).ToList();
        var lastDay = rowDays[^1];
        var bars = new List<CalendarBar>();
        var covered = new HashSet<CalendarItemViewModel>();
        var culture = CultureInfo.GetCultureInfo("en-GB");

        // Items lasting more than one day.
        foreach (var item in items.Where(i => (!onlyDayLong || i.IsDayLong) && LastDay(i) > FirstDay(i)))
        {
            var columns = rowDays.Select((d, n) => (d, n)).Where(x => item.OccursOn(x.d)).Select(x => x.n).ToList();
            if (columns.Count == 0)
            {
                continue;
            }
            var first = columns[0];
            var span = columns[^1] - first + 1;
            bars.Add(new CalendarBar(first, span, item.Title, item.IsDayLong ? null : item.Start.ToString("HH:mm", culture),
                item.CategoryBrushKey, item.ToolTip, [.. Enumerable.Repeat(new BarSegment(item), span)],
                FirstDay(item) < rowStart, LastDay(item) > lastDay, item.IsCancelled) { Key = item.Item.Id });
            covered.Add(item);
        }

        // The days of a planned session week, joined into one bar per row.
        if (joinSessionWeeks)
        {
            var groups = items.Where(i => i.Item.SessionWeekId is not null && !covered.Contains(i) && !i.IsDayLong)
                .GroupBy(i => i.Item.SessionWeekId!.Value);
            foreach (var group in groups)
            {
                var byColumn = rowDays.Select((d, n) => (n, Item: group.Where(i => i.OccursOn(d)).OrderBy(i => i.Start).FirstOrDefault()))
                    .Where(x => x.Item is not null).ToList();
                if (byColumn.Count < 2)
                {
                    continue; // a single day stays an ordinary item
                }
                var first = byColumn[0].n;
                var span = byColumn[^1].n - first + 1;
                var sample = byColumn[0].Item!;
                var segments = Enumerable.Range(first, span)
                    .Select(c => new BarSegment(byColumn.FirstOrDefault(x => x.n == c).Item)).ToList();
                var name = sample.Item.SessionName ?? sample.Title;
                var speakers = group.Select(i => i.Item.Speaker).OfType<string>().Distinct().ToList();
                var toolTip = $"{name}\n{rowDays[first].ToString("ddd d MMM", culture)} – {rowDays[first + span - 1].ToString("ddd d MMM", culture)} · {sample.TimeText}"
                              + (speakers.Count > 0 ? $"\nSharing: {string.Join(", ", speakers)}" : string.Empty)
                              + "\n\n" + string.Join("\n", byColumn.Select(x => $"{x.Item!.Start.ToString("ddd", culture)} · {x.Item.Title}"));
                bars.Add(new CalendarBar(first, span, name, sample.Start.ToString("HH:mm", culture), sample.CategoryBrushKey, toolTip, segments,
                    false, false, byColumn.All(x => x.Item!.IsCancelled)) { Key = $"week:{group.Key}" });
                foreach (var x in byColumn)
                {
                    covered.Add(x.Item!);
                }
            }
        }

        // Lanes: longest and earliest first, each in the first lane free over its columns.
        var lanes = new List<bool[]>();
        foreach (var bar in bars.OrderBy(b => b.Column).ThenByDescending(b => b.Span))
        {
            var lane = lanes.FindIndex(l => Enumerable.Range(bar.Column, bar.Span).All(c => !l[c]));
            if (lane < 0)
            {
                lanes.Add(new bool[days]);
                lane = lanes.Count - 1;
            }
            for (var c = bar.Column; c < bar.Column + bar.Span; c++)
            {
                lanes[lane][c] = true;
            }
            bar.Lane = lane;
            bar.Margin = bar.MarginFor(top);
        }
        return ([.. bars.OrderBy(b => b.Lane).ThenBy(b => b.Column)], covered);
    }

    /// <summary>The space the bars of a row take below the day numbers.</summary>
    public static double SpaceFor(IReadOnlyList<CalendarBar> bars) =>
        bars.Count == 0 ? 0 : (bars.Max(b => b.Lane) + 1) * (CalendarBar.Height + CalendarBar.Gap);

    private static DateOnly FirstDay(CalendarItemViewModel i) => DateOnly.FromDateTime(i.Start);

    /// <summary>The last day the item touches (an end exactly at midnight belongs to the day before).</summary>
    private static DateOnly LastDay(CalendarItemViewModel i) =>
        DateOnly.FromDateTime(i.End > i.Start && i.End.TimeOfDay == TimeSpan.Zero ? i.End.AddTicks(-1) : i.End);
}

/// <summary>A week row of the month view: its seven day cells and the bars across them.</summary>
public sealed class MonthWeekRow(IReadOnlyList<MonthDayCell> cells, IReadOnlyList<CalendarBar> bars)
{
    public IReadOnlyList<MonthDayCell> Cells { get; } = cells;
    public IReadOnlyList<CalendarBar> Bars { get; } = bars;
}
