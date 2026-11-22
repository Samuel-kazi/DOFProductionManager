using DOF.Contracts.Responses;
using DOF.Desktop.ViewModels.Calendar;

namespace DOF.UnitTests;

public class CalendarLayoutTests
{
    private static TimedItemViewModel Slot(double start, double end) => new(null!, start, end);

    [Fact]
    public void Separate_items_each_get_the_full_width()
    {
        var items = new[] { Slot(540, 600), Slot(600, 660) };

        TimeSlotLayout.AssignLanes(items);

        Assert.All(items, i => Assert.Equal((0, 1), (i.Lane, i.LaneCount)));
    }

    [Fact]
    public void Overlapping_items_share_the_width_side_by_side()
    {
        var a = Slot(540, 600);
        var b = Slot(570, 660);
        var c = Slot(630, 690); // overlaps b only, but is part of the same cluster and can reuse a's lane

        TimeSlotLayout.AssignLanes([a, b, c]);

        Assert.Equal((0, 2), (a.Lane, a.LaneCount));
        Assert.Equal((1, 2), (b.Lane, b.LaneCount));
        Assert.Equal((0, 2), (c.Lane, c.LaneCount));
    }

    [Fact]
    public void Three_simultaneous_items_get_three_lanes()
    {
        var items = new[] { Slot(600, 660), Slot(600, 630), Slot(610, 700) };

        TimeSlotLayout.AssignLanes(items);

        Assert.Equal([0, 1, 2], items.Select(i => i.Lane).Order());
        Assert.All(items, i => Assert.Equal(3, i.LaneCount));
    }
}
