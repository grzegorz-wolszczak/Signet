using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
using Signet.App.Resources;

namespace Signet.App.ScreenPicker;

/// <summary>The outcome of <see cref="ScreenColorPicker.PickAsync"/>.</summary>
/// <param name="Color">The picked opaque color; <see langword="null"/> when cancelled or on failure.</param>
/// <param name="Failed">Whether the desktop could not be captured (as opposed to the user cancelling).</param>
public readonly record struct ScreenPickResult(Color? Color, bool Failed);

/// <summary>
/// The screen color picker: snapshots the desktop into memory, shows it frozen on borderless overlays (one per monitor)
/// with a pixel magnifier, and returns the color of the clicked pixel. The snapshot never touches the disk (the
/// Wayland portal writes a file, which is deleted right after decoding) and is released as soon as the picker closes.
/// </summary>
public static class ScreenColorPicker
{
    /// <summary>Whether this platform has a screen capturer at all.</summary>
    public static bool IsSupported => ScreenCapturers.ForCurrentPlatform() is not null;

    /// <summary>Lets the user pick a color from the screen.</summary>
    /// <param name="owner">A window on the monitor(s) to cover; supplies the screen list.</param>
    /// <param name="capturer">The capturer to use; the platform default when <see langword="null"/>.</param>
    public static async Task<ScreenPickResult> PickAsync(Window owner, IScreenCapturer? capturer = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        capturer ??= ScreenCapturers.ForCurrentPlatform();
        if (capturer is null || owner.Screens is not { } screens)
        {
            return new ScreenPickResult(null, true);
        }

        ScreenSnapshot? snapshot;
        try
        {
            snapshot = await capturer.CaptureAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Capturing the screen failed");
            snapshot = null;
        }

        if (snapshot is null)
        {
            return new ScreenPickResult(null, true);
        }

        TaskCompletionSource<Color?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<ScreenPickerWindow> overlays = [];
        using WriteableBitmap bitmap = CreateBitmap(snapshot);
        try
        {
            string hint = Strings.Get("ScreenPicker_Hint");
            foreach (Screen screen in screens.All)
            {
                ScreenPickerWindow overlay = new(snapshot, bitmap, screen, hint, color => result.TrySetResult(color));
                overlay.Closed += (_, _) => result.TrySetResult(null);
                overlays.Add(overlay);
                overlay.Show();
            }

            if (overlays.Count == 0)
            {
                return new ScreenPickResult(null, true);
            }

            overlays[0].Activate();
            Color? picked = await result.Task;
            return new ScreenPickResult(picked is { } c ? Color.FromRgb(c.R, c.G, c.B) : null, false);
        }
        finally
        {
            foreach (ScreenPickerWindow overlay in overlays)
            {
                overlay.Close();
            }

            owner.Activate();
        }
    }

    private static WriteableBitmap CreateBitmap(ScreenSnapshot snapshot)
    {
        WriteableBitmap bitmap = new(new PixelSize(snapshot.Width, snapshot.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int rowBytes = snapshot.Width * 4;
        for (int y = 0; y < snapshot.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(snapshot.Buffer, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
        }

        return bitmap;
    }
}
