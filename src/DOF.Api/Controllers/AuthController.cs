using DOF.Application.Auth;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Common;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DOF.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;

namespace DOF.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, UserProfileQueries profiles, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Signs in with email and password and returns an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ApiResponse<AuthTokensResponse>> Login(LoginRequest request, CancellationToken ct) =>
        ApiResponse<AuthTokensResponse>.Ok(await auth.LoginAsync(request, ct));

    /// <summary>Exchanges a refresh token for a new token pair. The old refresh token stops working.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ApiResponse<AuthTokensResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct) =>
        ApiResponse<AuthTokensResponse>.Ok(await auth.RefreshAsync(request, ct));

    /// <summary>Revokes a refresh token. Works even if the access token has already expired.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>The signed-in user with their roles and effective permissions.</summary>
    [HttpGet("me")]
    public async Task<ApiResponse<CurrentUserResponse>> Me(CancellationToken ct) =>
        ApiResponse<CurrentUserResponse>.Ok(await profiles.GetCurrentUserAsync(currentUser.RequiredUserId, ct));

    /// <summary>Changes the password. All existing sessions end; a new token pair is returned.</summary>
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ApiResponse<AuthTokensResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        ApiResponse<AuthTokensResponse>.Ok(await auth.ChangePasswordAsync(currentUser.RequiredUserId, request, ct));
}
