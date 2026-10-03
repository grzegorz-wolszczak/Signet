using System;
using System.IO;
using SkiaSharp;
using SysPath = System.IO.Path;

namespace Signet.App.Imaging;

/// <summary>
/// Resizes image files with SkiaSharp. The output format is chosen by the file extension; bilinear
/// sampling with mipmaps.
/// </summary>
public static class ImageResizer
{
    private const int LossyQuality = 90;

    /// <summary>
    /// Scales the image at <paramref name="path"/> to <paramref name="width"/>×<paramref name="height"/>
    /// pixels and overwrites the file. Returns <c>false</c> when the file could not be decoded or the
    /// dimensions are invalid.
    /// </summary>
    public static bool ResizeFile(string path, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (width < 1 || height < 1)
        {
            return false;
        }

        using SKBitmap? source = SKBitmap.Decode(path);
        if (source is null)
        {
            return false;
        }

        SKImageInfo info = new(width, height, source.ColorType, source.AlphaType);
        using SKSurface surface = SKSurface.Create(info);
        using (SKImage image = SKImage.FromBitmap(source))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(
                image,
                new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                paint: null);
        }

        (SKEncodedImageFormat format, int quality) = FormatFor(path);
        using SKImage snapshot = surface.Snapshot();
        using SKData encoded = snapshot.Encode(format, quality);
        if (encoded is null)
        {
            return false;
        }

        using FileStream output = File.Create(path);
        encoded.SaveTo(output);
        return true;
    }

    private static (SKEncodedImageFormat Format, int Quality) FormatFor(string path) =>
        SysPath.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jfif" => (SKEncodedImageFormat.Jpeg, LossyQuality),
            ".webp" => (SKEncodedImageFormat.Webp, LossyQuality),
            ".bmp" => (SKEncodedImageFormat.Bmp, 100),
            // SkiaSharp does not encode GIF — a single frame is saved as PNG (the animation is lost).
            _ => (SKEncodedImageFormat.Png, 100),
        };
}
