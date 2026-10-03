using System;
using System.Buffers.Binary;
using System.IO;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="ImageResource"/> — file size, dimensions from the header, cover flag.</summary>
public sealed class ImageResourceTests
{
    private static byte[] PngHeader(int width, int height)
    {
        byte[] data = new byte[24];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(data, 0);
        data[8] = 0x00;
        data[9] = 0x00;
        data[10] = 0x00;
        data[11] = 0x0D;
        data[12] = (byte)'I';
        data[13] = (byte)'H';
        data[14] = (byte)'D';
        data[15] = (byte)'R';
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20, 4), height);
        return data;
    }

    [Fact]
    public void Reads_dimensions_from_a_png_header()
    {
        using TempDir root = new();
        string full = root.Combine("Images", "cover.png");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, PngHeader(640, 480));

        ImageResource resource = new(root.Path, full);

        resource.Type.Should().Be(ResourceType.Image);
        resource.GetDimensions().Should().Be((640, 480));
        resource.FileSize.Should().Be(24);
    }

    [Fact]
    public void Reads_dimensions_from_a_gif_header()
    {
        using TempDir root = new();
        string full = root.Combine("a.gif");
        // "GIF89a" + width=3 (LE) + height=7 (LE) + padding
        File.WriteAllBytes(full, new byte[]
        {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            0x03, 0x00, 0x07, 0x00, 0x00, 0x00,
        });

        new ImageResource(root.Path, full).GetDimensions().Should().Be((3, 7));
    }

    [Fact]
    public void Unknown_format_yields_zero_dimensions()
    {
        using TempDir root = new();
        string full = root.Combine("a.bin");
        File.WriteAllBytes(full, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        new ImageResource(root.Path, full).GetDimensions().Should().Be((0, 0));
    }

    [Fact]
    public void Cover_flag_is_settable()
    {
        using TempDir root = new();
        string full = root.Combine("c.png");
        File.WriteAllBytes(full, PngHeader(1, 1));

        ImageResource resource = new(root.Path, full) { IsCoverImage = true };

        resource.IsCoverImage.Should().BeTrue();
    }

    [Fact]
    public void GetDpi_reads_png_phys_chunk_in_dots_per_inch()
    {
        using TempDir root = new();
        string full = root.Combine("dpi.png");
        // 300 DPI ≈ 11811 pixels / metre.
        File.WriteAllBytes(full, PngWithPhys(200, 100, 11811, 11811, unit: 1));

        new ImageResource(root.Path, full).GetDpi().Should().Be((300, 300));
    }

    [Fact]
    public void GetDpi_returns_zero_when_no_resolution_information()
    {
        using TempDir root = new();
        string full = root.Combine("nodpi.png");
        File.WriteAllBytes(full, PngHeader(10, 10));

        new ImageResource(root.Path, full).GetDpi().Should().Be((0, 0));
    }

    private static byte[] PngWithPhys(int width, int height, uint ppuX, uint ppuY, byte unit)
    {
        using MemoryStream stream = new();
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        WriteChunk(stream, "IHDR", ihdr);

        Span<byte> phys = stackalloc byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(phys[..4], ppuX);
        BinaryPrimitives.WriteUInt32BigEndian(phys.Slice(4, 4), ppuY);
        phys[8] = unit;
        WriteChunk(stream, "pHYs", phys);

        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]); // CRC (not verified by the reader)
    }
}
