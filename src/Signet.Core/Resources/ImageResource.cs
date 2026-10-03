using System;
using System.Buffers.Binary;
using System.IO;

namespace Signet.Core.Resources;

/// <summary>An image resource (any raster format).</summary>
/// <remarks>
/// The dimensions are read from the file header (PNG, GIF, JPEG, BMP, WebP) without decoding the whole image
/// and without depending on a graphics library. For unrecognized formats <c>(0, 0)</c> is returned.
/// </remarks>
public sealed class ImageResource : Resource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public ImageResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Image;

    /// <summary>Whether this image is the book cover (set by the OPF logic).</summary>
    public bool IsCoverImage { get; set; }

    /// <summary>The file size in bytes (0 if the file does not exist).</summary>
    public long FileSize => File.Exists(FullPath) ? new FileInfo(FullPath).Length : 0;

    /// <summary>The image dimensions in pixels read from the header; <c>(0, 0)</c> if unrecognized.</summary>
    public (int Width, int Height) GetDimensions()
    {
        try
        {
            byte[] header = ReadHeader(FullPath, 8192);
            return ImageHeader.ReadDimensions(header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The image resolution in DPI (horizontal, vertical) read from the header (PNG <c>pHYs</c>,
    /// JPEG JFIF <c>APP0</c>); <c>(0, 0)</c> if unknown or the unit is "aspect ratio".
    /// </summary>
    public (int X, int Y) GetDpi()
    {
        try
        {
            byte[] header = ReadHeader(FullPath, 8192);
            return ImageHeader.ReadDpi(header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Reports that the image file on disk was changed outside the model (e.g. "Image Resize" in
    /// the App layer) — raises <see cref="Resource.ResourceUpdatedOnDisk"/> so the tab
    /// refreshes its preview.
    /// </summary>
    public void NotifyUpdatedOnDisk() => RaiseResourceUpdatedOnDisk();

    /// <inheritdoc/>
    protected override bool LoadFromDisk()
    {
        RaiseResourceUpdatedOnDisk();
        return true;
    }

    private static byte[] ReadHeader(string path, int count)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[count];
        int total = 0;
        int read;
        while (total < count && (read = stream.Read(buffer, total, count - total)) > 0)
        {
            total += read;
        }

        if (total == count)
        {
            return buffer;
        }

        byte[] trimmed = new byte[total];
        Array.Copy(buffer, trimmed, total);
        return trimmed;
    }
}

/// <summary>Reads the image dimensions from the first bytes of the file (PNG, GIF, JPEG, BMP, WebP).</summary>
internal static class ImageHeader
{
    public static (int Width, int Height) ReadDimensions(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (TryPng(data, out (int, int) png))
        {
            return png;
        }

        if (TryGif(data, out (int, int) gif))
        {
            return gif;
        }

        if (TryBmp(data, out (int, int) bmp))
        {
            return bmp;
        }

        if (TryWebp(data, out (int, int) webp))
        {
            return webp;
        }

        if (TryJpeg(data, out (int, int) jpeg))
        {
            return jpeg;
        }

        return (0, 0);
    }

    /// <summary>The DPI resolution from the PNG (<c>pHYs</c>) or JPEG (JFIF <c>APP0</c>) header; <c>(0, 0)</c> if absent.</summary>
    public static (int X, int Y) ReadDpi(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (TryPngDpi(data, out (int, int) png))
        {
            return png;
        }

        if (TryJpegDpi(data, out (int, int) jpeg))
        {
            return jpeg;
        }

        return (0, 0);
    }

    private static bool TryPngDpi(byte[] d, out (int, int) dpi)
    {
        dpi = default;
        if (d.Length < 8 || d[0] != 0x89 || d[1] != 0x50 || d[2] != 0x4E || d[3] != 0x47)
        {
            return false;
        }

        int pos = 8;
        while (pos + 12 <= d.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            int dataStart = pos + 8;
            if (type == "pHYs" && length >= 9 && dataStart + 9 <= d.Length)
            {
                uint ppuX = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(dataStart, 4));
                uint ppuY = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(dataStart + 4, 4));
                byte unit = d[dataStart + 8];
                if (unit != 1)
                {
                    return false; // unit "unknown" = aspect ratio only, not DPI
                }

                dpi = ((int)Math.Round(ppuX / 39.3701), (int)Math.Round(ppuY / 39.3701));
                return dpi.Item1 > 0 || dpi.Item2 > 0;
            }

            if (type == "IDAT" || type == "IEND" || length < 0)
            {
                return false;
            }

            pos = dataStart + length + 4; // + CRC
        }

        return false;
    }

    private static bool TryJpegDpi(byte[] d, out (int, int) dpi)
    {
        dpi = default;
        if (d.Length < 18 || d[0] != 0xFF || d[1] != 0xD8 || d[2] != 0xFF || d[3] != 0xE0)
        {
            return false;
        }

        // APP0: 'JFIF\0' from offset 6, density unit at 13, Xdensity/Ydensity 14..17
        if (d[6] != (byte)'J' || d[7] != (byte)'F' || d[8] != (byte)'I' || d[9] != (byte)'F')
        {
            return false;
        }

        byte units = d[13];
        int xDensity = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(14, 2));
        int yDensity = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(16, 2));
        dpi = units switch
        {
            1 => (xDensity, yDensity),                                        // dots per inch
            2 => ((int)Math.Round(xDensity * 2.54), (int)Math.Round(yDensity * 2.54)), // dots per cm
            _ => (0, 0),
        };
        return dpi.Item1 > 0 || dpi.Item2 > 0;
    }

    private static bool TryPng(byte[] d, out (int, int) size)
    {
        size = default;
        if (d.Length < 24 || d[0] != 0x89 || d[1] != 0x50 || d[2] != 0x4E || d[3] != 0x47)
        {
            return false;
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(20, 4));
        size = (width, height);
        return true;
    }

    private static bool TryGif(byte[] d, out (int, int) size)
    {
        size = default;
        if (d.Length < 10 || d[0] != (byte)'G' || d[1] != (byte)'I' || d[2] != (byte)'F')
        {
            return false;
        }

        size = (BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(8, 2)));
        return true;
    }

    private static bool TryBmp(byte[] d, out (int, int) size)
    {
        size = default;
        if (d.Length < 26 || d[0] != (byte)'B' || d[1] != (byte)'M')
        {
            return false;
        }

        size = (BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(18, 4)),
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(22, 4))));
        return true;
    }

    private static bool TryWebp(byte[] d, out (int, int) size)
    {
        size = default;
        if (d.Length < 30 || d[0] != (byte)'R' || d[1] != (byte)'I' || d[2] != (byte)'F' || d[3] != (byte)'F' ||
            d[8] != (byte)'W' || d[9] != (byte)'E' || d[10] != (byte)'B' || d[11] != (byte)'P')
        {
            return false;
        }

        string fourCc = System.Text.Encoding.ASCII.GetString(d, 12, 4);
        switch (fourCc)
        {
            case "VP8 ":
                size = (BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(26, 2)) & 0x3FFF,
                        BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(28, 2)) & 0x3FFF);
                return true;
            case "VP8L":
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(21, 4));
                    size = ((int)((bits & 0x3FFF) + 1), (int)(((bits >> 14) & 0x3FFF) + 1));
                    return true;
                }
            case "VP8X":
                size = (1 + (d[24] | (d[25] << 8) | (d[26] << 16)),
                        1 + (d[27] | (d[28] << 8) | (d[29] << 16)));
                return true;
            default:
                return false;
        }
    }

    private static bool TryJpeg(byte[] d, out (int, int) size)
    {
        size = default;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return false;
        }

        int pos = 2;
        while (pos + 9 < d.Length)
        {
            if (d[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = d[pos + 1];
            if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                size = (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 7, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 5, 2)));
                return true;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 2, 2));
            pos += 2 + segmentLength;
        }

        return false;
    }
}
