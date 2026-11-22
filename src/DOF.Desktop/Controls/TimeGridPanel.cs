using Avalonia;
using Avalonia.Controls;
using DOF.Desktop.ViewModels.Calendar;

namespace DOF.Desktop.Controls;

/// <summary>
/// Lays out a day's timed items vertically by time and side by side by lane. Each child's data context
/// must implement <see cref="ITimeSlot"/>.
/// </summary>
public sealed class TimeGridPanel : Panel
{
    public static readonly StyledProperty<double> HourHeightProperty =
        AvaloniaProperty.Register<TimeGridPanel, double>(nameof(HourHeight), 48);

    public const double MinimumItemHeight = 20;

    static TimeGridPanel() => AffectsMeasure<TimeGridPanel>(HourHeightProperty);

    public double HourHeight
    {
        get => GetValue(HourHeightProperty);
        set => SetValue(HourHeightProperty, value);
    }

    private double MinuteHeight => HourHeight / 60;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width;
        foreach (var child in Children)
        {
            if (child.DataContext is ITimeSlot slot)
            {
                child.Measure(new Size(width / slot.LaneCount, SlotHeight(slot)));
            }
        }
        return new Size(width, 24 * HourHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child.DataContext is not ITimeSlot slot)
            {
                child.Arrange(default);
                continue;
            }

            var laneWidth = finalSize.Width / slot.LaneCount;
            child.Arrange(new Rect(slot.Lane * laneWidth, slot.StartMinute * MinuteHeight,
                Math.Max(0, laneWidth - 2), SlotHeight(slot)));
        }
        return finalSize;
    }

    private double SlotHeight(ITimeSlot slot) =>
        Math.Max(MinimumItemHeight, (slot.EndMinute - slot.StartMinute) * MinuteHeight - 1);
}
