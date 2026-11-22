namespace DOF.Desktop.Services.Security;

public static class SecureStoreFactory
{
    public const string ServiceName = "DOF Production Manager";

    public static ISecureStore CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainStore(ServiceName);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore(ServiceName);
        }

        throw new PlatformNotSupportedException("Secure credential storage is available on macOS and Windows only.");
    }
}
