using System.Runtime.InteropServices;

namespace NativeWebView.Platform.Linux;

// Signet: pages without a declared encoding are read as UTF-8. See UPSTREAM.md ("UTF-8 as the default encoding").
//
// WebKitGTK's "default-charset" setting is ISO-8859-1. An (X)HTML file served through file:// with an .html extension
// is parsed as HTML, where the XML declaration's encoding="utf-8" counts for nothing; without a <meta charset> or a BOM
// WebKit fell back to ISO-8859-1, and every Polish letter of a UTF-8 page showed as two or three Latin-1 characters
// ("sÅ‚oneczny"). WebView2 on Windows detects UTF-8 there, so the same book looked right on Windows.
public sealed partial class LinuxNativeWebViewBackend
{
    internal const string DefaultCharset = "utf-8";

    // Called on the GTK thread once the web view's settings exist.
    internal static void ApplySignetDefaultCharset(IntPtr settings)
    {
        webkit_settings_set_default_charset(settings, DefaultCharset);
    }

    [DllImport("libwebkit2gtk-4.1.so.0")]
    private static extern void webkit_settings_set_default_charset(IntPtr settings, [MarshalAs(UnmanagedType.LPUTF8Str)] string charset);
}
