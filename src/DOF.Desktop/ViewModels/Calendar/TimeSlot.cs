namespace DOF.Desktop.ViewModels.Calendar;

/// <summary>Where an item sits in a day's time grid. Read by <c>Controls.TimeGridPanel</c>.</summary>
public interface ITimeSlot
{
    double StartMinute { get; }
    double EndMinute { get; }
    int Lane { get; }
    int LaneCount { get; }
}

/// <summary>A timed item's portion of one day, positioned for the week and day views.</summary>
public sealed class TimedItemViewModel(CalendarItemViewModel item, double startMinute, double endMinute) : ITimeSlot
{
    public CalendarItemViewModel Item { get; } = item;
    public double StartMinute { get; } = startMinute;
    public double EndMinute { get; } = endMinute;
    public int Lane { get; internal set; }
    public int LaneCount { get; internal set; } = 1;
    public DateOnly Day { get; init; }
}

public static class TimeSlotLayout
{
    /// <summary>
    /// Places overlapping items side by side. Items that overlap (directly or through a chain) form a
    /// cluster; each gets the first free lane, and every item in a cluster shares its lane count.
    /// </summary>
    public static void AssignLanes(IReadOnlyList<TimedItemViewModel> items)
    {
        var ordered = items.OrderBy(i => i.StartMinute).ThenByDescending(i => i.EndMinute).ToList();
        var cluster = new List<TimedItemViewModel>();
        var laneEnds = new List<double>();
        var clusterEnd = double.MinValue;

        foreach (var item in ordered)
        {
            if (item.StartMinute >= clusterEnd && cluster.Count > 0)
            {
                Close(cluster, laneEnds.Count);
                cluster.Clear();
                laneEnds.Clear();
            }

            var lane = laneEnds.FindIndex(end => end <= item.StartMinute);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(item.EndMinute);
            }
            else
            {
                laneEnds[lane] = item.EndMinute;
            }

            item.Lane = lane;
            cluster.Add(item);
            clusterEnd = Math.Max(cluster.Count == 1 ? item.EndMinute : clusterEnd, item.EndMinute);
        }

        Close(cluster, laneEnds.Count);
    }

    private static void Close(List<TimedItemViewModel> cluster, int lanes)
    {
        foreach (var member in cluster)
        {
            member.LaneCount = Math.Max(1, lanes);
        }
    }
}
