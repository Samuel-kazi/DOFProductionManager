using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DOF.Desktop.Services.Security;

/// <summary>Stores secrets as generic passwords in the user's login keychain.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainStore(string serviceName) : ISecureStore
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;

    private readonly byte[] _service = Encoding.UTF8.GetBytes(serviceName);

    public string? Get(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)_service.Length, _service,
            (uint)account.Length, account, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return null;
        }
        ThrowOnError(status, "read");

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    public void Set(string key, string value)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var secret = Encoding.UTF8.GetBytes(value);

        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)_service.Length, _service,
            (uint)account.Length, account, out _, out var existingData, out var item);

        if (status == ErrSecItemNotFound)
        {
            status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)_service.Length, _service,
                (uint)account.Length, account, (uint)secret.Length, secret, out var added);
            ThrowOnError(status, "store");
            CFRelease(added);
            return;
        }
        ThrowOnError(status, "read");

        try
        {
            ThrowOnError(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret), "update");
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, existingData);
            CFRelease(item);
        }
    }

    public void Remove(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)_service.Length, _service,
            (uint)account.Length, account, out _, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return;
        }
        ThrowOnError(status, "read");

        try
        {
            ThrowOnError(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    private static void ThrowOnError(int status, string operation)
    {
        if (status != ErrSecSuccess)
        {
            throw new InvalidOperationException($"Keychain {operation} failed (OSStatus {status}).");
        }
    }

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList,
        uint length, byte[] data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(IntPtr cf);
}
