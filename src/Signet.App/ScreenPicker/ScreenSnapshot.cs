using System;
using Avalonia.Media;

namespace Signet.App.ScreenPicker;

/// <summary>
/// An in-memory copy of the desktop (BGRA, 4 bytes per pixel, row-major, top-down), kept only while the screen color
/// picker is open. Coordinates are desktop pixels: (<see cref="OriginX"/>, <see cref="OriginY"/>) is the desktop
/// position of the first pixel (it can be negative on a multi-monitor layout).
/// </summary>
public sealed class ScreenSnapshot
{
    private readonly byte[] _bgra;

    /// <summary>Creates a snapshot over <paramref name="bgra"/> (not copied).</summary>
    /// <param name="bgra">Pixels, <c>width * height * 4</c> bytes, blue first.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="originX">Desktop X of the left edge.</param>
    /// <param name="originY">Desktop Y of the top edge.</param>
    public ScreenSnapshot(byte[] bgra, int width, int height, int originX, int originY)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bgra.Length < (long)width * height * 4)
        {
            throw new ArgumentException("The buffer is smaller than width * height * 4.", nameof(bgra));
        }

        _bgra = bgra;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Desktop X of the left edge.</summary>
    public int OriginX { get; }

    /// <summary>Desktop Y of the top edge.</summary>
    public int OriginY { get; }

    /// <summary>The raw BGRA pixels.</summary>
    public ReadOnlySpan<byte> Pixels => _bgra;

    /// <summary>The raw BGRA buffer (for building a bitmap).</summary>
    internal byte[] Buffer => _bgra;

    /// <summary>Whether the desktop pixel (<paramref name="x"/>, <paramref name="y"/>) lies inside the snapshot.</summary>
    public bool Contains(int x, int y) =>
        x >= OriginX && y >= OriginY && x < OriginX + Width && y < OriginY + Height;

    /// <summary>The opaque color of a desktop pixel; <see cref="Colors.Transparent"/> outside the snapshot.</summary>
    public Color GetPixel(int x, int y)
    {
        if (!Contains(x, y))
        {
            return Colors.Transparent;
        }

        int i = (((y - OriginY) * Width) + (x - OriginX)) * 4;
        return Color.FromRgb(_bgra[i + 2], _bgra[i + 1], _bgra[i]);
    }

    /// <summary>
    /// The <paramref name="size"/> × <paramref name="size"/> block of pixels centred on a desktop pixel, row-major
    /// (pixels outside the snapshot are <see cref="Colors.Transparent"/>). The centre is at index
    /// <c>(size / 2) * size + size / 2</c> for an odd <paramref name="size"/>.
    /// </summary>
    public Color[] GetRegion(int centerX, int centerY, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        Color[] result = new Color[size * size];
        int half = size / 2;
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                result[(row * size) + column] = GetPixel(centerX - half + column, centerY - half + row);
            }
        }

        return result;
    }
}
