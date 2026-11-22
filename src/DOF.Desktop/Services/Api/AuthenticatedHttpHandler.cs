using System.Net;
using System.Net.Http.Headers;

namespace DOF.Desktop.Services.Api;

/// <summary>Adds the current access token to every API request, refreshing it first when needed.</summary>
public sealed class AuthenticatedHttpHandler(AuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await session.GetAccessTokenAsync(ct);
        if (token is null)
        {
            // No session any more: answer locally exactly as the server would.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}
