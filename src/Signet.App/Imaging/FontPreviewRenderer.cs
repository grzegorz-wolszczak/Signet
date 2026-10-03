using System.Collections.Generic;
using System.IO;
using System;
using Avalonia.Media.Imaging;
using Signet.App.Resources;
using SkiaSharp;

namespace Signet.App.Imaging;

/// <summary>
/// Renders a typeface sample (alphabet, digits, pangram in several sizes) to a bitmap using
/// the font file itself — without a WebView and without registering the font in the system.
/// </summary>
public static class FontPreviewRenderer
{
    private static string[] SampleLines =>
    [
        "abcdefghijklmnopqrstuvwxyz",
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "0123456789  .,:;!?  ($&@)",
        Strings.Get("FontTab_Pangram"),
    ];

    private static readonly float[] PangramSizes = { 14f, 20f, 28f, 40f };

    /// <summary>
    /// Returns a bitmap with a typeface sample from the file <paramref name="fontPath"/>, or <c>null</c> when
    /// SkiaSharp cannot load the font (e.g. WOFF2).
    /// </summary>
    /// <param name="fontPath">Font file path (TTF / OTF).</param>
    /// <param name="dark">Whether to render the dark variant (light text on a dark background).</param>
    public static Bitmap? Render(string fontPath, bool dark)
    {
        ArgumentNullException.ThrowIfNull(fontPath);
        try
        {
            return RenderCore(fontPath, dark);
        }
        catch (Exception)
        {
            // No graphics platform (unit test context) or a corrupt font —
            // the preview is optional, so return null instead of crashing the tab.
            return null;
        }
    }

    private static Bitmap? RenderCore(string fontPath, bool dark)
    {
        using SKTypeface? typeface = LoadTypeface(fontPath);
        if (typeface is null)
        {
            return null;
        }

        const int width = 720;
        SKColor background = dark ? new SKColor(0x1E, 0x1E, 0x1E) : SKColors.White;
        SKColor foreground = dark ? new SKColor(0xE6, 0xE6, 0xE6) : new SKColor(0x1A, 0x1A, 0x1A);

        List<(string Text, float Size)> rows = new();
        foreach (string line in SampleLines)
        {
            rows.Add((line, 18f));
        }

        foreach (float size in PangramSizes)
        {
            rows.Add((SampleLines[3], size));
        }

        float y = 12f;
        List<(string Text, float Size, float Y)> placed = new();
        foreach ((string text, float size) in rows)
        {
            y += size + 8f;
            placed.Add((text, size, y));
        }

        int height = (int)Math.Ceiling(y + 16f);

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(background);

        using SKPaint paint = new() { Color = foreground, IsAntialias = true };
        foreach ((string text, float size, float rowY) in placed)
        {
            using SKFont font = new(typeface, size);
            canvas.DrawText(text, 12f, rowY, SKTextAlign.Left, font, paint);
        }

        using SKImage snapshot = surface.Snapshot();
        using SKData data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        return new Bitmap(stream);
    }

    private static SKTypeface? LoadTypeface(string fontPath)
    {
        try
        {
            return SKTypeface.FromFile(fontPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
