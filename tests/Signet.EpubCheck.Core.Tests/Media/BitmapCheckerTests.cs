using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Media;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Media;

/// <summary>The checks of GIF, JPEG and PNG images: header, format against extension and type, size.</summary>
public sealed class BitmapCheckerTests
{
    [Theory]
    [InlineData("image/png", "a.png")]
    [InlineData("image/gif", "a.gif")]
    [InlineData("image/jpeg", "a.jpg")]
    [InlineData("image/jpeg", "a.jpeg")]
    public void Check_ValidImage_ReportsNothing(string mimeType, string name)
    {
        // Arrange
        ContentDocumentHarness harness = Image(name, mimeType, Bytes(mimeType, 10, 20));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Theory]
    [InlineData("image/png", "a.png", 3840, 100, true)]
    [InlineData("image/png", "a.png", 100, 2160, true)]
    [InlineData("image/png", "a.png", 3839, 2159, false)]
    [InlineData("image/gif", "a.gif", 4000, 10, true)]
    [InlineData("image/jpeg", "a.jpg", 10, 3000, true)]
    public void Check_ImageLargerThanTheLimit_ReportsOpf051(string mimeType, string name, int width, int height, bool reported)
    {
        // Arrange
        ContentDocumentHarness harness = Image(name, mimeType, Bytes(mimeType, width, height));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Contains(MessageId.OPF_051).Should().Be(reported);
    }

    [Fact]
    public void Check_ImageOfFourMegabytes_ReportsOpf057()
    {
        // Arrange
        byte[] bytes = [.. Bytes("image/png", 10, 10), .. new byte[4 * 1024 * 1024]];
        ContentDocumentHarness harness = Image("big.png", "image/png", bytes);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.OPF_057);
    }

    [Fact]
    public void Check_JpegDeclaredAsGif_ReportsOpf029()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.gif", "image/gif", Bytes("image/jpeg", 10, 10));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.OPF_029);
        harness.Messages(MessageId.OPF_029).Single().Arguments.Should().Equal("a.gif", "image/gif");
    }

    [Fact]
    public void Check_JpegWithAGifExtension_ReportsPkg022()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.gif", "image/jpeg", Bytes("image/jpeg", 10, 10));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.PKG_022);
        harness.Messages(MessageId.PKG_022).Single().Arguments.Should().Equal("JPEG", "gif");
    }

    [Fact]
    public void Check_FileShorterThanItsHeader_ReportsMed004AndPkg021()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.png", "image/png", [0x89, 0x50]);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.MED_004, MessageId.PKG_021);
    }

    [Fact]
    public void Check_TruncatedImage_ReportsPkg021()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.png", "image/png", Bytes("image/png", 10, 10)[..16]);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.PKG_021);
    }

    [Fact]
    public void Check_FileWithoutAnExtension_ReportsPkg021()
    {
        // Arrange
        ContentDocumentHarness harness = Image("image", "image/png", Bytes("image/png", 10, 10));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.PKG_021);
    }

    [Fact]
    public void Check_ExtensionInADifferentCase_IsNotOneOfTheReaders()
    {
        // Arrange (ImageIO lists "png" and "PNG" only)
        ContentDocumentHarness harness = Image("a.Png", "image/png", Bytes("image/png", 10, 10));

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Equal(MessageId.PKG_021);
    }

    [Fact]
    public void Check_ImageThatIsNotInTheContainer_ReportsRsc001()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.png", "image/png", Bytes("image/png", 10, 10));
        harness.Build();

        // Act
        new BitmapChecker(harness.Context with { Url = harness.Local("missing.png") }).Check();

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_001);
    }

    [Fact]
    public void CheckerFactory_GivesTheBitmapCheckerToGifJpegAndPng()
    {
        // Arrange
        ContentDocumentHarness harness = Image("a.png", "image/png", Bytes("image/png", 10, 10));
        harness.Build();

        // Act & Assert
        foreach (string type in new[] { "image/gif", "image/jpeg", "image/png" })
        {
            Core.Opf.CheckerFactory.NewChecker(harness.Context with { MimeType = type }).Should().BeOfType<BitmapChecker>();
        }

        Core.Opf.CheckerFactory.NewChecker(harness.Context with { MimeType = "image/webp" }).Should().NotBeOfType<BitmapChecker>();
    }

    private static ContentDocumentHarness Image(string name, string mimeType, byte[] bytes) => new(name, bytes, mimeType);

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new BitmapChecker(harness.Context).Check();
    }

    // The smallest header of an image of the type with the given size.
    private static byte[] Bytes(string mimeType, int width, int height) => mimeType switch
    {
        "image/png" =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, 6, 0, 0, 0,
        ],
        "image/gif" =>
        [
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0, 0,
            0x2C, 0, 0, 0, 0, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0,
        ],
        _ =>
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0,
        ],
    };
}
