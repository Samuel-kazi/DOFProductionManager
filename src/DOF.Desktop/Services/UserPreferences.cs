using System.Text.Json;

namespace DOF.Desktop.Services;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Per-user, non-sensitive UI preferences. Never put credentials here.</summary>
public sealed record UserPreferences
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public bool SidebarCollapsed { get; init; }
    public string? LastEmail { get; init; }
    /// <summary>The calendar view opened first (Month, Week, Day or Agenda).</summary>
    public string DefaultCalendarView { get; init; } = "Month";
}

/// <summary>Stores <see cref="UserPreferences"/> as JSON in the user's application-data folder.</summary>
public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;

    public PreferencesStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOFProductionManager");
        _path = Path.Combine(directory, "preferences.json");
        Current = Read();
    }

    public UserPreferences Current { get; private set; }

    public void Update(Func<UserPreferences, UserPreferences> change)
    {
        Current = change(Current);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
    }

    private UserPreferences Read()
    {
        if (!File.Exists(_path))
        {
            return new UserPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), Json) ?? new UserPreferences();
        }
        catch (JsonException ex)
        {
            // A damaged preferences file must not stop the app from starting; fall back to defaults.
            AppLog.Warning($"Ignoring unreadable preferences file {_path}: {ex.Message}");
            return new UserPreferences();
        }
    }
}
