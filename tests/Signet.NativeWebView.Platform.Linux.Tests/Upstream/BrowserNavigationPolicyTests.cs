// Ported from NativeWebView's tests/NativeWebView.Core.Tests/BrowserNavigationPolicyTests.cs (commit 1b6d498, MIT - see ../LICENSE): the tests of
// the Linux backend only (the file also tests other platforms' backends, which are not part of the fork).
using NativeWebView.Core;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Upstream;

public class BrowserNavigationPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinuxUsesFinalizedJavaScriptPolicy(bool enabled)
    {
        using var backend = new LinuxNativeWebViewBackend();
        backend.CoreWebView2ControllerOptionsRequested += (_, e) => e.Options.IsJavaScriptEnabled = enabled;
        await backend.InitializeAsync();
        Assert.Equal(enabled, backend.IsPageJavaScriptEnabled);
    }
}
