// Ported from NativeWebView's tests/NativeWebView.Core.Tests/SnapshotSupportTests.cs (commit 1b6d498, MIT - see ../LICENSE): the tests of
// the Linux backend only (the file also tests other platforms' backends, which are not part of the fork).
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Upstream;

public sealed class SnapshotSupportTests
{
    [Fact]
    public void LinuxGtkEnqueue_ThrowsWhenIdleSourceRegistrationFails()
    {
        var actionInvoked = false;

        Assert.Throws<InvalidOperationException>(() =>
            LinuxNativeInterop.EnqueueOnGtkThread(
                () => actionInvoked = true,
                static _ => 0));

        Assert.False(actionInvoked);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void LinuxMouseStatus_IsRejectedDuringOrAfterTeardown(
        bool isDisposed,
        bool isRuntimeInitialized,
        bool expected)
    {
        Assert.Equal(
            expected,
            LinuxNativeWebViewBackend.CanAcceptMouseStatus(isDisposed, isRuntimeInitialized));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void LinuxRuntime_IsNotReadyUntilInitializationAndNativeViewExist(
        bool isRuntimeInitialized,
        bool hasWebView,
        bool expected)
    {
        Assert.Equal(
            expected,
            LinuxNativeWebViewBackend.IsRuntimeReady(isRuntimeInitialized, hasWebView));
    }

    [Fact]
    public void LinuxRuntimeNavigationLifecycle_DoesNotFinishNewNavigationForOlderFailure()
    {
        var lifecycle = new LinuxRuntimeNavigationLifecycle();
        var failedNavigationId = lifecycle.StartNavigation();
        Assert.Equal(failedNavigationId, lifecycle.FailNavigation());
        var currentNavigationId = lifecycle.StartNavigation();

        Assert.False(lifecycle.TryFinishNavigation(out _));
        Assert.True(lifecycle.TryFinishNavigation(out var finishedNavigationId));
        Assert.Equal(currentNavigationId, finishedNavigationId);
    }

    [Fact]
    public void LinuxRuntimeNavigationLifecycle_DropsFailedTerminalStateWhenRuntimeIsDestroyed()
    {
        var lifecycle = new LinuxRuntimeNavigationLifecycle();
        lifecycle.StartNavigation();
        lifecycle.FailNavigation();

        lifecycle.RuntimeDestroyed();
        var currentNavigationId = lifecycle.StartNavigation();

        Assert.True(lifecycle.TryFinishNavigation(out var finishedNavigationId));
        Assert.Equal(currentNavigationId, finishedNavigationId);
    }

    [Fact]
    public void LinuxSignalCleanup_ContinuesAfterIndividualDisposalFailure()
    {
        var first = new TestDisposable(throwOnDispose: true);
        var second = new TestDisposable(throwOnDispose: false);

        LinuxNativeWebViewBackend.DisposeSubscriptions([first, second]);

        Assert.True(first.WasDisposed);
        Assert.True(second.WasDisposed);
    }

    private sealed class TestDisposable(bool throwOnDispose) : IDisposable
    {
        public bool WasDisposed { get; private set; }

        public void Dispose()
        {
            WasDisposed = true;
            if (throwOnDispose)
                throw new InvalidOperationException("Test disposal failure.");
        }
    }
}
