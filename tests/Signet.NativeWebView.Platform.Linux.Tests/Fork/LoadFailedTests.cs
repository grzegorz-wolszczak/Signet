using System.Collections.Concurrent;
using AutoFixture;
using AwesomeAssertions;
using NativeWebView.Core;
using NativeWebView.Interop;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// A page that fails to load: the backend's "load-failed" handler must leave the signal's GError to WebKit. Upstream freed
/// it, and WebKit's own handler (webkitWebViewLoadFail, which shows the error page) then read the freed message - a
/// SIGSEGV on the GTK thread, reported as "stack smashing detected". Found in November 2026: Signet died after merging
/// HTML files in the Book Browser with the Preview open (the shown file left the preview mirror, so its page failed to
/// load). UPSTREAM.md, "A signal's GError is not freed".
/// Needs WebKitGTK and an X11 display; skipped without them. Without the fix this test ends the test process.
/// </summary>
[Collection(ForkStaticStateCollection.Name)]
public sealed class LoadFailedTests
{
    [Fact]
    public async Task A_page_that_fails_to_load_shows_WebKit_s_error_page()
    {
        // Arrange
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            "Needs Linux with an X11 display.");
        var cancellation = TestContext.Current.CancellationToken;
        // The parent of the web view's window: realized, never shown, so nothing appears on the screen.
        var parent = await LinuxGtkDispatcher.InvokeAsync(LinuxNativeWebViewBackend.CreateHostWindowOnGtkThread, cancellation);
        var completions = new BlockingCollection<NativeWebViewNavigationCompletedEventArgs>();
        try
        {
            using var backend = new LinuxNativeWebViewBackend();
            var configuration = new NativeWebViewInstanceConfiguration();
            configuration.ControllerOptions.IsInPrivateModeEnabled = true; // an ephemeral context: nothing written to disk
            backend.ApplyInstanceConfiguration(configuration);
            backend.NavigationCompleted += (_, e) => completions.Add(e, CancellationToken.None);
            backend.AttachToNativeParent(new NativePlatformHandle(parent.Xid, "XID"));
            await backend.InitializeAsync(cancellation);
            var missing = new Uri($"file:///nonexistent-{new Fixture().Create<Guid>():N}/page.html");

            // Act
            backend.Navigate(missing);

            // Assert
            completions.TryTake(out var failed, TimeSpan.FromSeconds(20)).Should().BeTrue("the navigation completes");
            failed!.IsSuccess.Should().BeFalse();
            failed.Error.Should().NotBeNullOrWhiteSpace("the message is read before WebKit frees the error");
            completions.TryTake(out var errorPage, TimeSpan.FromSeconds(20))
                .Should().BeTrue("WebKit's handler, run after the backend's, loads its error page from the error message");
            errorPage!.IsSuccess.Should().BeTrue();
        }
        finally
        {
            await LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.gtk_widget_destroy(parent.GtkWindow), CancellationToken.None);
        }
    }
}
