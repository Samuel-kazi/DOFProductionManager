using DOF.Application.Common.Abstractions;
using DOF.Application.Common.Exceptions;
using DOF.Contracts.Requests;
using DOF.Contracts.Responses;
using DOF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOF.Application.Auth;

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    UserProfileQueries profiles,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    // Verifying against a throwaway hash when the email is unknown keeps response times similar,
    // so the endpoint does not reveal which email addresses have accounts.
    private static string? _dummyHash;

    public async Task<AuthTokensResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            hasher.Verify(_dummyHash ??= hasher.Hash(Guid.NewGuid().ToString()), request.Password);
            throw new AuthenticationFailedException();
        }

        var verification = hasher.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerification.Failed || !user.IsActive)
        {
            logger.LogInformation("Failed login for user {UserId} (active: {IsActive})", user.Id, user.IsActive);
            throw new AuthenticationFailedException();
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.Hash(request.Password));
        }

        logger.LogInformation("User {UserId} logged in", user.Id);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), request.DeviceName, ct);
    }

    public async Task<AuthTokensResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var stored = await FindRefreshTokenAsync(request.RefreshToken, ct)
                     ?? throw new AuthenticationFailedException("The session is invalid or has expired.");

        if (stored.IsRevoked)
        {
            if (stored.RevocationReason == RefreshToken.Reasons.Rotated)
            {
                // A token that was already exchanged is being replayed: treat the whole login as compromised.
                await RevokeFamilyAsync(stored.FamilyId, RefreshToken.Reasons.ReuseDetected, now, ct);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", stored.UserId);
            }

            throw new AuthenticationFailedException("The session is invalid or has expired.");
        }

        if (!stored.IsUsable(now))
        {
            throw new AuthenticationFailedException("The session is invalid or has expired.");
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive)
        {
            stored.Revoke(now, RefreshToken.Reasons.UserDeactivated);
            await db.SaveChangesAsync(ct);
            throw new AuthenticationFailedException("The session is invalid or has expired.");
        }

        return await IssueTokensAsync(user, stored.FamilyId, stored.DeviceName, ct, replacing: stored);
    }

    /// <summary>Revokes the given refresh token. Idempotent: unknown or already revoked tokens succeed silently.</summary>
    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var stored = await FindRefreshTokenAsync(request.RefreshToken, ct);
        if (stored is not null && !stored.IsRevoked)
        {
            stored.Revoke(clock.GetUtcNow(), RefreshToken.Reasons.LoggedOut);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User {UserId} logged out", stored.UserId);
        }
    }

    /// <summary>Changes the password, ends every existing session and returns a fresh one.</summary>
    public async Task<AuthTokensResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException("User", userId);

        if (hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerification.Failed)
        {
            throw new AuthenticationFailedException("The current password is incorrect.");
        }

        user.SetPasswordHash(hasher.Hash(request.NewPassword));
        await RevokeAllForUserAsync(db, userId, RefreshToken.Reasons.PasswordChanged, clock.GetUtcNow(), ct);

        logger.LogInformation("User {UserId} changed their password", userId);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), deviceName: null, ct);
    }

    internal static async Task RevokeAllForUserAsync(
        IApplicationDbContext db, Guid userId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.Revoke(now, reason);
        }
    }

    private async Task<AuthTokensResponse> IssueTokensAsync(
        User user, Guid familyId, string? deviceName, CancellationToken ct, RefreshToken? replacing = null)
    {
        var now = clock.GetUtcNow();
        var refresh = tokens.CreateRefreshToken();
        var stored = new RefreshToken(user.Id, refresh.TokenHash, familyId, now + tokens.RefreshTokenLifetime, deviceName);
        db.RefreshTokens.Add(stored);
        replacing?.Revoke(now, RefreshToken.Reasons.Rotated, stored.Id);

        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        var profile = await profiles.GetCurrentUserAsync(user.Id, ct);
        return new AuthTokensResponse(access.Token, access.ExpiresAt, refresh.Token, stored.ExpiresAt, profile);
    }

    private Task<RefreshToken?> FindRefreshTokenAsync(string token, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(token);
        return db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in family)
        {
            token.Revoke(now, reason);
        }
        await db.SaveChangesAsync(ct);
    }
}
