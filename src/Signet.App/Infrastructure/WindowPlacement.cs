using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Remembering where windows were and making sure a window restored from remembered coordinates (or moved by
/// docking) is really on screen: the resolution or the monitors may have changed since. A window is fully visible
/// when its top-left and its bottom-right corner each lie in the working area (the screen without the taskbar) of
/// some monitor; otherwise it is moved to the center of the primary monitor's working area and shrunk to fit.
/// </summary>
/// <remarks>
/// Positions are physical pixels (<see cref="Window.Position"/>), sizes logical units (<c>Window.Width</c>/<c>Height</c>),
/// as <see cref="WindowGeometry"/> stores them.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>Whether both the top-left and the bottom-right corner of <paramref name="rect"/> lie in one of the areas.</summary>
    public static bool IsFullyVisible(PixelRect rect, IReadOnlyList<PixelRect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        return rect.Width > 0 && rect.Height > 0
            && Contains(workingAreas, rect.X, rect.Y)
            && Contains(workingAreas, rect.X + rect.Width - 1, rect.Y + rect.Height - 1);
    }

    /// <summary><paramref name="rect"/> shrunk to fit <paramref name="area"/> and centered in it.</summary>
    public static PixelRect CenterIn(PixelRect rect, PixelRect area)
    {
        int width = Math.Clamp(rect.Width, 1, Math.Max(1, area.Width));
        int height = Math.Clamp(rect.Height, 1, Math.Max(1, area.Height));
        return new PixelRect(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height);
    }

    /// <summary>
    /// The corrected placement of <paramref name="rect"/> — centered in <paramref name="primaryArea"/> — or
    /// <c>null</c> when it is fully visible as it is.
    /// </summary>
    public static PixelRect? Correct(PixelRect rect, IReadOnlyList<PixelRect> workingAreas, PixelRect primaryArea) =>
        IsFullyVisible(rect, workingAreas) ? null : CenterIn(rect, primaryArea);

    /// <summary>
    /// Restores <paramref name="window"/> (not shown yet) to <paramref name="geometry"/>, corrected when it would not
    /// be fully visible. A maximized or full-screen window keeps its state (on the primary monitor after a correction).
    /// </summary>
    public static void Restore(Window window, WindowGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (geometry.Width <= 0 || geometry.Height <= 0)
        {
            return;
        }

        double width = Math.Max(geometry.Width, window.MinWidth);
        double height = Math.Max(geometry.Height, window.MinHeight);
        PixelPoint position = new(geometry.X, geometry.Y);

        if (Monitors(window) is { } monitors)
        {
            double scaling = monitors.Screens.ScreenFromPoint(position)?.Scaling ?? monitors.Primary.Scaling;
            PixelRect rect = new(position, PixelSize.FromSize(new Size(width, height), scaling));
            if (Correct(rect, monitors.WorkingAreas, monitors.Primary.WorkingArea) is { } corrected)
            {
                DebugLog.Write("Window", $"{window.GetType().Name}: remembered place {rect} is not fully on screen → {corrected}");
                position = corrected.Position;
                width = corrected.Width / monitors.Primary.Scaling;
                height = corrected.Height / monitors.Primary.Scaling;
            }
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = position;
        window.Width = width;
        window.Height = height;
        window.WindowState = geometry switch
        {
            { FullScreen: true } => WindowState.FullScreen,
            { Maximized: true } => WindowState.Maximized,
            _ => WindowState.Normal,
        };
    }

    /// <summary>The current geometry of <paramref name="window"/>, for <see cref="Restore"/>.</summary>
    public static WindowGeometry Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new WindowGeometry(
            window.Position.X,
            window.Position.Y,
            (int)window.Width,
            (int)window.Height,
            Maximized: window.WindowState == WindowState.Maximized,
            FullScreen: window.WindowState == WindowState.FullScreen);
    }

    /// <summary>
    /// Moves a shown <paramref name="window"/> to the center of the primary monitor (shrunk to fit) when it is not
    /// fully visible. Returns whether it was moved.
    /// </summary>
    public static bool EnsureVisible(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState != WindowState.Normal || Monitors(window) is not { } monitors)
        {
            return false;
        }

        double scaling = window.DesktopScaling;
        Size size = window.FrameSize ?? window.ClientSize;
        PixelRect rect = new(window.Position, PixelSize.FromSize(size, scaling));
        if (Correct(rect, monitors.WorkingAreas, monitors.Primary.WorkingArea) is not { } corrected)
        {
            return false;
        }

        DebugLog.Write("Window", $"{window.GetType().Name} \"{window.Title}\": {rect} is not fully on screen → {corrected}");

        if (corrected.Width < rect.Width || corrected.Height < rect.Height)
        {
            double primaryScaling = monitors.Primary.Scaling;
            window.Width = Math.Min(window.Width, corrected.Width / primaryScaling);
            window.Height = Math.Min(window.Height, corrected.Height / primaryScaling);
        }

        window.Position = corrected.Position;
        return true;
    }

    /// <summary>
    /// Restores <paramref name="window"/> (not shown yet) from the geometry remembered under <paramref name="key"/>
    /// (when there is one) and remembers its geometry again when it closes.
    /// </summary>
    public static void Remember(Window window, string key, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetWindowGeometry(key) is { } geometry)
        {
            Restore(window, geometry);
        }

        window.Closing += (_, _) =>
        {
            settings.SetWindowGeometry(key, Capture(window));
            settings.Save();
        };
    }

    private sealed record MonitorInfo(Screens Screens, Screen Primary, IReadOnlyList<PixelRect> WorkingAreas);

    // The monitors the window can be placed on, or null when the platform reports none (e.g. headless).
    private static MonitorInfo? Monitors(Window window)
    {
        if (window.Screens is not { } screens || screens.All.Count == 0)
        {
            return null;
        }

        Screen primary = screens.Primary ?? screens.All[0];
        return new MonitorInfo(screens, primary, screens.All.Select(s => s.WorkingArea).ToList());
    }

    private static bool Contains(IReadOnlyList<PixelRect> areas, int x, int y) =>
        areas.Any(a => x >= a.X && x < a.X + a.Width && y >= a.Y && y < a.Y + a.Height);
}
