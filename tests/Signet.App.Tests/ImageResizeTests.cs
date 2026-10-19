using System.IO;
using AwesomeAssertions;
using Signet.App.Imaging;
using Signet.Core.Tests.TestSupport;
using SkiaSharp;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ImageResizer"/> ("Image Resize" via SkiaSharp).</summary>
public sealed class ImageResizeTests
{
    private static string WritePng(string path, int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static (int Width, int Height) DecodeSize(string path)
    {
        using SKBitmap decoded = SKBitmap.Decode(path);
        return (decoded.Width, decoded.Height);
    }

    [Fact]
    public void ResizeFile_scales_a_png_and_overwrites_it()
    {
        // Arrange
        using TempDir temp = new();
        string path = WritePng(temp.Combine("pic.png"), 120, 80);

        // Act & Assert
        ImageResizer.ResizeFile(path, 60, 40).Should().BeTrue();

        // Assert
        DecodeSize(path).Should().Be((60, 40));
    }

    [Fact]
    public void ResizeFile_scales_a_jpeg_and_keeps_the_format()
    {
        // Arrange
        using TempDir temp = new();
        string path = temp.Combine("pic.jpg");
        using (SKBitmap bitmap = new(64, 64))
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        // Act & Assert
        ImageResizer.ResizeFile(path, 32, 48).Should().BeTrue();

        // Assert
        DecodeSize(path).Should().Be((32, 48));

        // Act
        using SKCodec codec = SKCodec.Create(path);

        // Assert
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);
    }

    [Fact]
    public void ResizeFile_rejects_invalid_dimensions()
    {
        // Arrange
        using TempDir temp = new();
        string path = WritePng(temp.Combine("pic.png"), 10, 10);

        // Act & Assert
        ImageResizer.ResizeFile(path, 0, 10).Should().BeFalse();

        // Assert
        DecodeSize(path).Should().Be((10, 10));
    }

    [Fact]
    public void ResizeFile_returns_false_for_a_non_image_file()
    {
        // Arrange
        using TempDir temp = new();
        string path = temp.Combine("not-an-image.png");
        File.WriteAllText(path, "definitely not a PNG");

        // Act & Assert
        ImageResizer.ResizeFile(path, 20, 20).Should().BeFalse();
    }
}
