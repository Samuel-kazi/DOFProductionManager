using System.Security.Cryptography;
using System.Text;
using DOF.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace DOF.Infrastructure.GoogleCalendar;

/// <summary>
/// AES-256-GCM with the key in <c>GoogleCalendar:TokenEncryptionKey</c> (base64, 32 bytes, from the
/// environment or a secret store). Output is base64url of nonce | tag | ciphertext; the purpose is bound
/// in as associated data so a value made for one use cannot be replayed for another.
/// </summary>
public sealed class AesSecretProtector(IOptions<GoogleCalendarOptions> options) : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool IsConfigured => Key() is not null;

    public string Protect(string plaintext, string purpose)
    {
        var key = Key() ?? throw new InvalidOperationException("GoogleCalendar:TokenEncryptionKey is not configured.");
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(purpose));
        return Convert.ToBase64String(output).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        var key = Key() ?? throw new CryptographicException("GoogleCalendar:TokenEncryptionKey is not configured.");
        byte[] data;
        try
        {
            var base64 = protectedValue.Replace('-', '+').Replace('_', '/');
            data = Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected value is malformed.", ex);
        }
        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected value is malformed.");
        }

        var plain = new byte[data.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain,
            Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    private byte[]? Key()
    {
        var configured = options.Value.TokenEncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }
        try
        {
            var key = Convert.FromBase64String(configured);
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
