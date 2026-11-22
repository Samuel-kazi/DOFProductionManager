using DOF.Desktop.Services.Security;

namespace DOF.IntegrationTests.Desktop;

/// <summary>Exercises the real macOS login keychain. Skipped on other platforms.</summary>
public class MacKeychainStoreTests
{
    [Fact]
    public void Stores_updates_reads_and_removes_secrets()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainStore($"DOF Production Manager Tests {Guid.NewGuid():N}");
        const string key = "refresh-token";
        try
        {
            Assert.Null(store.Get(key));

            store.Set(key, "first-value");
            Assert.Equal("first-value", store.Get(key));

            store.Set(key, "second-value-ünïcødé");
            Assert.Equal("second-value-ünïcødé", store.Get(key));

            store.Remove(key);
            Assert.Null(store.Get(key));
            store.Remove(key);
        }
        finally
        {
            store.Remove(key);
        }
    }
}
