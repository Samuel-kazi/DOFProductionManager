using DOF.Application.Common.Abstractions;
using DOF.Domain.Entities;
using DOF.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DOF.UnitTests;

public class AuthenticationInfrastructureTests
{
    private static readonly JwtOptions Options = new() { SigningKey = new string('k', 40) };

    [Fact]
    public void Password_hashes_verify_only_the_original_password()
    {
        var hasher = new IdentityPasswordHasher();

        var hash = hasher.Hash("Correct-Horse-Battery");

        Assert.NotEqual("Correct-Horse-Battery", hash);
        Assert.Equal(PasswordVerification.Success, hasher.Verify(hash, "Correct-Horse-Battery"));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify(hash, "correct-horse-battery"));
        Assert.NotEqual(hash, hasher.Hash("Correct-Horse-Battery"));
    }

    [Fact]
    public void Access_tokens_carry_the_user_and_expire_after_the_configured_lifetime()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero));
        var service = new TokenService(Microsoft.Extensions.Options.Options.Create(Options), clock);
        var user = User.Create("Ada", "ada@dof.org", "hash", "Africa/Nairobi");

        var issued = service.CreateAccessToken(user);

        Assert.Equal(clock.GetUtcNow().AddMinutes(15), issued.ExpiresAt);
        var jwt = new JsonWebToken(issued.Token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("ada@dof.org", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(Options.Issuer, jwt.Issuer);
    }

    [Fact]
    public void Refresh_tokens_are_random_and_stored_as_hashes()
    {
        var service = new TokenService(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.Token, first.TokenHash);
        Assert.Equal(64, first.TokenHash.Length);
        Assert.Equal(first.TokenHash, service.HashRefreshToken(first.Token));
    }

    [Theory]
    [InlineData("too-short", 15, 14, false)]
    [InlineData("a-signing-key-that-is-at-least-32-bytes", 15, 14, true)]
    [InlineData("a-signing-key-that-is-at-least-32-bytes", 0, 14, false)]
    [InlineData("a-signing-key-that-is-at-least-32-bytes", 15, 365, false)]
    public void Jwt_options_are_validated(string key, int accessMinutes, int refreshDays, bool expected)
    {
        var options = new JwtOptions { SigningKey = key, AccessTokenMinutes = accessMinutes, RefreshTokenDays = refreshDays };

        Assert.Equal(expected, options.IsValid());
    }
}
