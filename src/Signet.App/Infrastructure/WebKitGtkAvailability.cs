using System;
using System.Runtime.InteropServices;

namespace Signet.App.Infrastructure;

/// <summary>
/// Tells whether the WebKitGTK runtime the Linux Preview needs is installed. Without it the Preview panel stays an
/// empty, transparent host instead of failing, so the application checks for the library itself.
/// </summary>
public static class WebKitGtkAvailability
{
    /// <summary>File name of the library the forked NativeWebView backend loads.</summary>
    public const string LibraryName = "libwebkit2gtk-4.1.so.0";

    private static readonly Lazy<bool> Probe = new(ProbeLibrary);

    /// <summary>False only on Linux when WebKitGTK 4.1 cannot be loaded; always true on other systems.</summary>
    public static bool IsAvailable => !OperatingSystem.IsLinux() || Probe.Value;

    private static bool ProbeLibrary()
    {
        try
        {
            if (!NativeLibrary.TryLoad(LibraryName, out IntPtr handle))
            {
                return false;
            }

            NativeLibrary.Free(handle);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
