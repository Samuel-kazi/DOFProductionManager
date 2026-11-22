namespace DOF.Contracts.Responses;

public sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserResponse User);

public sealed record CurrentUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string TimeZone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
