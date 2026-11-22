namespace DOF.Desktop.Services;

/// <summary>
/// Minimal file log for the desktop client (one file per day, kept 14 days), so failures on users'
/// machines can be diagnosed. Location: the app-data folder's <c>logs</c> directory.
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOFProductionManager", "logs");

    public static void Info(string message) => Write("INF", message);

    public static void Warning(string message) => Write("WRN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
        System.Diagnostics.Trace.Write(line);
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, $"desktop-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch (IOException)
        {
            // Logging must never crash the app; the Trace output above still carries the message.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void PruneOldFiles(TimeSpan maxAge)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "desktop-*.log"))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > maxAge)
            {
                File.Delete(file);
            }
        }
    }
}
