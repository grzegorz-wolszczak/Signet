using System;
using System.Threading.Tasks;

namespace Signet.App.ScreenPicker;

/// <summary>Chooses the capturer for the current platform and session type.</summary>
public static class ScreenCapturers
{
    /// <summary>
    /// Windows → GDI; Linux Wayland session → xdg-desktop-portal Screenshot; Linux X11 session → Xlib, falling back to
    /// the portal when Xlib fails. Other platforms have none (<see langword="null"/>).
    /// </summary>
    public static IScreenCapturer? ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsScreenCapturer();
        }

        if (OperatingSystem.IsLinux())
        {
            return IsWaylandSession()
                ? new PortalScreenCapturer()
                : new FallbackScreenCapturer(new X11ScreenCapturer(), new PortalScreenCapturer());
        }

        return null;
    }

    /// <summary>Whether the session is Wayland (Avalonia then runs on XWayland, whose root window cannot be read).</summary>
    public static bool IsWaylandSession() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
}

/// <summary>Tries the primary capturer, then the secondary one when the primary returns nothing or throws.</summary>
internal sealed class FallbackScreenCapturer(IScreenCapturer primary, IScreenCapturer secondary) : IScreenCapturer
{
    public async Task<ScreenSnapshot?> CaptureAsync()
    {
        try
        {
            if (await primary.CaptureAsync().ConfigureAwait(false) is { } snapshot)
            {
                return snapshot;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Serilog.Log.Warning(exception, "Primary screen capture failed, trying the fallback");
        }

        return await secondary.CaptureAsync().ConfigureAwait(false);
    }
}
