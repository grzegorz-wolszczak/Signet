using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System;
using Signet.App.Resources;

namespace Signet.App.Services;

/// <summary>
/// Opens a file in the default system application. Used by tabs without a built-in
/// player / viewer (audio, video, PDF).
/// </summary>
public static class ExternalOpen
{
    /// <summary>
    /// Tries to open <paramref name="fullPath"/> in the system application. Returns <c>false</c>
    /// (and fills <paramref name="error"/>) when the file does not exist or the shell refused.
    /// </summary>
    public static bool TryOpen(string fullPath, out string? error)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        if (!File.Exists(fullPath))
        {
            error = Strings.Get("ExternalOpen_FileMissing");
            return false;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }
}
