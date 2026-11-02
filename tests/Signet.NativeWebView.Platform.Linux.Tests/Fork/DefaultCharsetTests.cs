using System.Runtime.InteropServices;
using AwesomeAssertions;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// The web view reads pages that declare no encoding as UTF-8: WebKitGTK's default, ISO-8859-1, turned the Polish letters
/// of an .html chapter without a &lt;meta charset&gt; into Latin-1 garbage in Signet's preview (UPSTREAM.md, "UTF-8 as
/// the default encoding"). Needs GTK and an X11 display; skipped without them.
/// </summary>
public sealed class DefaultCharsetTests
{
    [Fact]
    public async Task The_web_view_settings_read_pages_without_a_declared_encoding_as_UTF_8()
    {
        // Arrange
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            "Needs Linux with an X11 display.");
        var cancellation = TestContext.Current.CancellationToken;

        // Act
        var charsets = await LinuxGtkDispatcher.InvokeAsync(
            () =>
            {
                var settings = webkit_settings_new();
                try
                {
                    var before = Marshal.PtrToStringUTF8(webkit_settings_get_default_charset(settings));
                    LinuxNativeWebViewBackend.ApplySignetDefaultCharset(settings);
                    var after = Marshal.PtrToStringUTF8(webkit_settings_get_default_charset(settings));
                    return (Before: before, After: after);
                }
                finally
                {
                    g_object_unref(settings);
                }
            },
            cancellation);

        // Assert
        charsets.Before.Should().BeEquivalentTo("iso-8859-1", "this is the WebKitGTK default the fork overrides");
        charsets.After.Should().Be("utf-8");
    }

    [DllImport("libwebkit2gtk-4.1.so.0")]
    private static extern IntPtr webkit_settings_new();

    [DllImport("libwebkit2gtk-4.1.so.0")]
    private static extern IntPtr webkit_settings_get_default_charset(IntPtr settings);

    [DllImport("libgobject-2.0.so.0")]
    private static extern void g_object_unref(IntPtr instance);
}
