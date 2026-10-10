using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Signet.Core.Localization;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Raster image checks: a damaged or incomplete file and a CMYK JPEG (many readers show those with wrong colours or
/// not at all). Called from <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// <c>Signet.Core</c> does not decode images (SkiaSharp lives in <c>Signet.App</c>), so "damaged" means what the file
/// structure shows without decoding: the header cannot be read, or the end of the file is missing (a PNG without its
/// <c>IEND</c> chunk, a JPEG without the end-of-image marker, a GIF without its trailer, a WebP shorter than its RIFF
/// header says). Only PNG, JPEG, GIF and WebP files are checked; the type comes from the content, not the name.
/// </remarks>
public static class ImageIntegrityValidator
{
    // How far from the end of the file the end marker may be (some tools append a few bytes after it).
    private const int EndMarkerWindow = 64;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private enum Format
    {
        Unknown,
        Png,
        Jpeg,
        Gif,
        Webp,
    }

    /// <summary>Runs the image checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (Resource resource in book.GetAllResources())
        {
            if (resource is not ImageResource image)
            {
                continue;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(image.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            CheckImage(image, data, results);
        }

        return results;
    }

    private static void CheckImage(ImageResource image, byte[] data, List<ValidationResult> results)
    {
        Format format = Detect(data);
        if (format == Format.Unknown)
        {
            return;
        }

        if (!IsComplete(format, data))
        {
            results.Add(new ValidationResult(
                ValidationSeverity.Error, image.BookPath, -1, -1,
                CoreStrings.Format("Validation_ImageCorrupt", image.Filename), "Validation_ImageCorrupt"));
            return;
        }

        if (format == Format.Jpeg && JpegComponents(data) == 4)
        {
            results.Add(new ValidationResult(
                ValidationSeverity.Warning, image.BookPath, -1, -1,
                CoreStrings.Format("Validation_ImageCmyk", image.Filename), "Validation_ImageCmyk"));
        }
    }

    /// <summary>Whether the file has a readable header and its end marker.</summary>
    internal static bool IsComplete(byte[] data) => Detect(data) is var format && format != Format.Unknown && IsComplete(format, data);

    private static bool IsComplete(Format format, byte[] data)
    {
        if (ImageHeader.ReadDimensions(data) is not (> 0, > 0))
        {
            return false;
        }

        return format switch
        {
            Format.Png => EndsWith(data, "IEND"u8),
            Format.Jpeg => EndsWith(data, [0xFF, 0xD9]),
            Format.Gif => EndsWith(data, [0x3B]),
            Format.Webp => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) + 8L <= data.Length,
            _ => true,
        };
    }

    private static Format Detect(byte[] data)
    {
        ReadOnlySpan<byte> span = data;
        if (span.StartsWith(PngSignature))
        {
            return Format.Png;
        }

        if (span.StartsWith(JpegSignature))
        {
            return Format.Jpeg;
        }

        if (span.StartsWith("GIF8"u8))
        {
            return Format.Gif;
        }

        return span.Length >= 12 && span.StartsWith("RIFF"u8) && span[8..12].SequenceEqual("WEBP"u8)
            ? Format.Webp
            : Format.Unknown;
    }

    private static bool EndsWith(byte[] data, ReadOnlySpan<byte> marker)
    {
        int start = Math.Max(0, data.Length - EndMarkerWindow);
        return data.AsSpan(start).LastIndexOf(marker) >= 0;
    }

    /// <summary>The number of colour components in the first SOF segment of a JPEG; <c>0</c> when there is none.</summary>
    internal static int JpegComponents(byte[] data)
    {
        int i = 2;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF)
            {
                return 0;
            }

            byte marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 2, 2));

            // SOF0..SOF15 without DHT (C4), JPG (C8) and DAC (CC): precision, height, width, components.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return i + 9 < data.Length ? data[i + 9] : 0;
            }

            if (marker == 0xDA)
            {
                return 0;
            }

            i += 2 + length;
        }

        return 0;
    }
}
