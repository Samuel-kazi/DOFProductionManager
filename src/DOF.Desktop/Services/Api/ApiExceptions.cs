using System.Net;
using System.Text.Json;

namespace DOF.Desktop.Services.Api;

/// <summary>The server answered with an error. <see cref="Message"/> is suitable to show to the user.</summary>
public sealed class ApiException(
    HttpStatusCode status, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public IReadOnlyDictionary<string, string[]> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string[]>();

    public static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? title = null, detail = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                errors = e.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => p.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray());
            }
        }
        catch (JsonException)
        {
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.TooManyRequests => "Too many requests. Please wait a minute and try again.",
            HttpStatusCode.BadRequest when errors is { Count: > 0 } =>
                string.Join(" ", errors.SelectMany(kv => kv.Value)),
            >= HttpStatusCode.InternalServerError => "The server ran into a problem. Please try again shortly.",
            _ => detail ?? title ?? $"The request failed ({(int)response.StatusCode}).",
        };

        return new ApiException(response.StatusCode, message, errors);
    }
}

/// <summary>The server could not be reached (offline, DNS, refused, timed out).</summary>
public sealed class ApiUnavailableException(Exception inner)
    : Exception("Can't reach the DOF server. Check your connection and try again.", inner);
