using DOF.Application.GoogleCalendar;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DOF.Api.Controllers;

/// <summary>
/// The organisation's Google Calendar connection. Anyone signed in can see whether it is connected;
/// connecting and disconnecting need settings.manage.
/// </summary>
[ApiController]
[Route("api/v1/integrations/google-calendar")]
public sealed class GoogleCalendarController(GoogleConnectionService connections) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResponse<GoogleCalendarStatusResponse>> Status(CancellationToken ct) =>
        ApiResponse<GoogleCalendarStatusResponse>.Ok(await connections.GetStatusAsync(ct));

    /// <summary>Returns the Google sign-in address to open in the browser; Google redirects back to <c>redirectUri</c>.</summary>
    [HttpPost("authorize")]
    [Authorize(Policy = Permissions.SettingsManage)]
    public ApiResponse<GoogleAuthorizationResponse> Authorize(StartGoogleAuthorizationRequest request) =>
        ApiResponse<GoogleAuthorizationResponse>.Ok(connections.StartAuthorization(request.RedirectUri));

    /// <summary>Completes the sign-in with the code and state Google returned, and stores the connection.</summary>
    [HttpPost("connect")]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<ApiResponse<GoogleCalendarStatusResponse>> Connect(CompleteGoogleAuthorizationRequest request, CancellationToken ct) =>
        ApiResponse<GoogleCalendarStatusResponse>.Ok(await connections.CompleteAuthorizationAsync(request.Code, request.State, ct));

    /// <summary>Stops using the account and revokes DOF's access. Events already sent stay on guests' calendars.</summary>
    [HttpDelete]
    [Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await connections.DisconnectAsync(ct);
        return NoContent();
    }
}
