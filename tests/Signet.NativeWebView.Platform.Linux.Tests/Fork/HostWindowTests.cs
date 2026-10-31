using System.Runtime.InteropServices;
using AwesomeAssertions;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// The GTK window hosting the web view is created realized but not shown: shown as a window of its own before it was
/// reparented into Avalonia's native control host, it was kept by the compositor (Cinnamon) as a separate window - a
/// transparent hole over the preview (UPSTREAM.md, "The GTK host window is not shown before it is embedded").
/// Needs GTK and an X11 display; skipped without them. The window is never mapped, so nothing appears on the screen.
/// </summary>
public sealed class HostWindowTests
{
    [Fact]
    public async Task The_GTK_host_window_is_realized_but_not_shown_before_it_is_embedded()
    {
        // Arrange
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            "Needs Linux with an X11 display.");
        var cancellation = TestContext.Current.CancellationToken;

        // Act
        var state = await LinuxGtkDispatcher.InvokeAsync(
            () =>
            {
                var host = LinuxNativeWebViewBackend.CreateHostWindowOnGtkThread();
                try
                {
                    return (Xid: host.Xid, Realized: gtk_widget_get_realized(host.GtkWindow), Visible: gtk_widget_get_visible(host.GtkWindow));
                }
                finally
                {
                    LinuxNativeInterop.gtk_widget_destroy(host.GtkWindow);
                }
            },
            cancellation);

        // Assert
        state.Xid.Should().NotBe(0, "the window is realized: it has an X11 window to reparent");
        state.Realized.Should().BeTrue();
        state.Visible.Should().BeFalse("it is shown only once embedded, with the web view inside");
    }

    [DllImport("libgtk-3.so.0")]
    private static extern bool gtk_widget_get_visible(IntPtr widget);

    [DllImport("libgtk-3.so.0")]
    private static extern bool gtk_widget_get_realized(IntPtr widget);
}
