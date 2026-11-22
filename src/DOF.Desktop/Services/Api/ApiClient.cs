using System.Net;
using System.Net.Http.Json;
using DOF.Contracts.Common;

namespace DOF.Desktop.Services.Api;

/// <summary>
/// The single entry point for authenticated calls to the DOF API. Unwraps the standard response envelope,
/// converts errors into <see cref="ApiException"/> / <see cref="ApiUnavailableException"/>, keeps the
/// connection indicator current and announces when the session has ended.
/// </summary>
public sealed class ApiClient(HttpClient http, ConnectionMonitor connection)
{
    /// <summary>Raised when the server rejects the session (expired, revoked or account deactivated).</summary>
    public event EventHandler? SessionExpired;

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), ct);

    public Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }, ct);

    public Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) }, ct);

    public Task<T> PatchAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body) }, ct);

    /// <summary>Downloads a file (for example a CSV report) as bytes.</summary>
    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiUnavailableException(ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(response.StatusCode, $"The download failed ({(int)response.StatusCode}).");
            }
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    /// <summary>A PATCH without a body to an endpoint that answers 204 No Content.</summary>
    public async Task PatchNoContentAsync(string path, CancellationToken ct = default) =>
        await SendAsync<object?>(new HttpRequestMessage(HttpMethod.Patch, path), ct, expectBody: false);

    /// <summary>For endpoints that answer 204 No Content.</summary>
    public async Task DeleteAsync(string path, CancellationToken ct = default) =>
        await SendAsync<object?>(new HttpRequestMessage(HttpMethod.Delete, path), ct, expectBody: false);

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct, bool expectBody = true)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await http.SendAsync(request, ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            connection.ReportFailure();
            throw new ApiUnavailableException(ex);
        }

        using (response)
        {
            connection.ReportSuccess();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SessionExpired?.Invoke(this, EventArgs.Empty);
                throw new ApiException(HttpStatusCode.Unauthorized, "Your session has ended. Please sign in again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ApiException.FromResponseAsync(response, ct);
            }

            if (!expectBody)
            {
                return default!;
            }

            var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(ct);
            return body is { Data: not null } ? body.Data
                : throw new ApiException(response.StatusCode, "The server returned an empty response.");
        }
    }
}
