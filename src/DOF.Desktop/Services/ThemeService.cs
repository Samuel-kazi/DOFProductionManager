using Avalonia;
using Avalonia.Styling;

namespace DOF.Desktop.Services;

public interface IThemeService
{
    void Apply(ThemePreference preference);
}

public sealed class ThemeService : IThemeService
{
    public void Apply(ThemePreference preference)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = preference switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
