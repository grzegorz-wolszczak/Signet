using System;
using System.Runtime.InteropServices;

namespace Signet.App.Infrastructure;

/// <summary>
/// Environment WebKitGTK needs on Linux, set before the engine starts. In a Wayland session (KDE Plasma on Fedora) the
/// GTK window embedded through XWayland stays grey with the DMABUF renderer: the page loads but is never painted.
/// </summary>
internal static class WebKitEnvironment
{
    private const string DmabufVariable = "WEBKIT_DISABLE_DMABUF_RENDERER";

    /// <summary>Switches the DMABUF renderer off in a Wayland session, unless the variable is already set.</summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsLinux() || !IsWaylandSession() || Environment.GetEnvironmentVariable(DmabufVariable) is not null)
        {
            return;
        }

        // Environment.SetEnvironmentVariable changes only the managed copy on Unix; WebKitGTK reads the native one.
        _ = setenv(DmabufVariable, "1", 0);
    }

    private static bool IsWaylandSession() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    // DllImport, not LibraryImport: the latter would need AllowUnsafeBlocks for one call.
#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "setenv", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int setenv(string name, string value, int overwrite);
#pragma warning restore SYSLIB1054
}
