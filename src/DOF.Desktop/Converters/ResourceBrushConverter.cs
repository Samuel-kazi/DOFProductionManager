using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DOF.Desktop.Converters;

/// <summary>
/// Looks up a brush by resource key in the current theme (e.g. category colours). The brush is resolved
/// when the view is built; pages are rebuilt on navigation, so a theme change applies on the next visit.
/// </summary>
public sealed class ResourceBrushConverter : IValueConverter
{
    public static ResourceBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is not { } app)
        {
            return null;
        }

        return app.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : app.TryGetResource("Brush.Cat.Other", app.ActualThemeVariant, out var fallback) ? fallback : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
