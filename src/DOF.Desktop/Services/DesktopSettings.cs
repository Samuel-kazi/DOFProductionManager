using System.Text.Json;

namespace DOF.Desktop.Services;

/// <summary>
/// Deployment settings read from <c>appsettings.json</c> next to the executable.
/// <c>DOF_API_URL</c> overrides the API address (useful for testing against another server).
/// <c>ExportFolder</c> is where downloaded PDFs are offered to be saved (default: Documents).
/// </summary>
public sealed record DesktopSettings(Uri ApiBaseUrl, string? ExportFolder = null)
{
    /// <summary>The configured export folder if it exists, otherwise the user's Documents folder.</summary>
    public string ExportDirectory =>
        ExportFolder is { Length: > 0 } folder && Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public const string ApiUrlEnvironmentVariable = "DOF_API_URL";

    public static DesktopSettings Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, "appsettings.json");
        string? configured = null;
        string? exportFolder = null;
        if (File.Exists(path))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.TryGetProperty("ApiBaseUrl", out var value))
            {
                configured = value.GetString();
            }
            if (json.RootElement.TryGetProperty("ExportFolder", out var folder))
            {
                exportFolder = folder.GetString();
            }
        }

        var url = Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable) ?? configured
                  ?? throw new InvalidOperationException($"ApiBaseUrl is not configured in {path}.");
        return FromUrl(url) with { ExportFolder = exportFolder };
    }

    /// <summary>
    /// Passwords and tokens travel to this address, so anything other than a local development server
    /// must use HTTPS.
    /// </summary>
    public static DesktopSettings FromUrl(string url)
    {
        if (!Uri.TryCreate(url.EndsWith('/') ? url : url + "/", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"ApiBaseUrl '{url}' is not a valid URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new InvalidOperationException($"ApiBaseUrl '{url}' must use HTTPS.");
        }

        return new DesktopSettings(uri);
    }
}
