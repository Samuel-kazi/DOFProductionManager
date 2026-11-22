using DOF.Domain.Entities;

namespace DOF.Application.Common.Abstractions;

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record IssuedRefreshToken(string Token, string TokenHash);

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    IssuedAccessToken CreateAccessToken(User user);

    IssuedRefreshToken CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
