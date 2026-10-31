// Ported from NativeWebView's tests/NativeWebView.Core.Tests/ProfileStorageConfigurationTests.cs (commit 1b6d498, MIT - see ../LICENSE): the tests of
// the Linux backend only (the file also tests other platforms' backends, which are not part of the fork).
using NativeWebView.Core;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Upstream;

public sealed class ProfileStorageConfigurationTests
{
    [Fact]
    public void LinuxPersistentStorage_MapsConfiguredDataAndCacheDirectories()
    {
        var configuration = new NativeWebViewEnvironmentOptions
        {
            UserDataFolder = Path.GetFullPath("profile-data"),
            CacheFolder = Path.GetFullPath("profile-cache"),
        };

        var storage = LinuxNativeWebViewBackend.ResolvePersistentStorageConfiguration(configuration);

        Assert.NotNull(storage);
        Assert.Equal(configuration.UserDataFolder, storage.BaseDataDirectory);
        Assert.Equal(configuration.CacheFolder, storage.BaseCacheDirectory);
    }

    [Fact]
    public void LinuxPersistentStorage_UsesSessionDirectoryWhenUserDataIsAbsent()
    {
        var configuration = new NativeWebViewEnvironmentOptions
        {
            SessionDataFolder = Path.GetFullPath("profile-session"),
        };

        var storage = LinuxNativeWebViewBackend.ResolvePersistentStorageConfiguration(configuration);

        Assert.NotNull(storage);
        Assert.Equal(configuration.SessionDataFolder, storage.BaseDataDirectory);
        Assert.Null(storage.BaseCacheDirectory);
    }
}
