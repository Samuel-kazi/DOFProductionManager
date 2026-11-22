namespace DOF.Domain.Entities;

/// <summary>
/// A long-lived credential used to obtain new access tokens. Only a hash of the token is stored.
/// Tokens rotate on every use; all tokens descended from one login share a <see cref="FamilyId"/>, so a
/// replayed (already rotated) token can revoke the whole family.
/// </summary>
public sealed class RefreshToken : Entity
{
    public static class Reasons
    {
        public const string Rotated = "Rotated";
        public const string LoggedOut = "LoggedOut";
        public const string ReuseDetected = "ReuseDetected";
        public const string PasswordChanged = "PasswordChanged";
        public const string UserDeactivated = "UserDeactivated";
    }

    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, Guid familyId, DateTimeOffset expiresAt, string? deviceName)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        DeviceName = deviceName;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? DeviceName { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsUsable(DateTimeOffset now) => !IsRevoked && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string reason, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
