using DOF.Domain.Entities;
using AspNetIdentity = Microsoft.AspNetCore.Identity;
using DOF.Application.Common.Abstractions;

namespace DOF.Infrastructure.Authentication;

/// <summary>Adapts ASP.NET Core Identity's password hasher (PBKDF2, versioned format).</summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly AspNetIdentity.PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public PasswordVerification Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            AspNetIdentity.PasswordVerificationResult.Success => PasswordVerification.Success,
            AspNetIdentity.PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed,
        };
}
