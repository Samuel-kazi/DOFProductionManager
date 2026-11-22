namespace DOF.Desktop.Services.Security;

/// <summary>
/// Operating-system credential storage (macOS Keychain, Windows Credential Manager).
/// Only long-lived secrets such as the refresh token are stored; access tokens stay in memory.
/// </summary>
public interface ISecureStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}
