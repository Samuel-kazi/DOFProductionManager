using System.Text;

namespace DOF.Infrastructure.Authentication;

/// <summary>
/// Token settings, bound from the <c>Jwt</c> configuration section. The signing key is a secret: set it
/// with user-secrets in development and an environment variable / secret store elsewhere.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "dof-production-manager";
    public string Audience { get; set; } = "dof-production-manager-clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    public bool IsValid() =>
        Encoding.UTF8.GetByteCount(SigningKey) >= MinimumKeyBytes
        && AccessTokenMinutes is > 0 and <= 60
        && RefreshTokenDays is > 0 and <= 90
        && !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience);
}
