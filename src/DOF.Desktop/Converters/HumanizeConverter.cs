using System.Globalization;
using Avalonia.Data.Converters;
using DOF.Desktop.ViewModels.Calendar;

namespace DOF.Desktop.Converters;

/// <summary>Shows code values such as "ExternalAppointment" as readable labels.</summary>
public sealed class HumanizeConverter : IValueConverter
{
    public static HumanizeConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? CalendarLabels.Humanize(text) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
