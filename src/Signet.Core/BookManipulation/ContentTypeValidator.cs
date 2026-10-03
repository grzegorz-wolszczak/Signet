using System;
using System.Collections.Generic;
using System.IO;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks that the declared media type matches the file's actual content (byte
/// signatures). Called from <see cref="BookValidator.ValidateCurrentBook"/>, like
/// <see cref="OpfStructureValidator"/>, <see cref="LinkIntegrityValidator"/>,
/// <see cref="FontIntegrityValidator"/>, <see cref="CrossFileStructureValidator"/> and
/// <see cref="FileNamePortabilityValidator"/>.
/// </summary>
/// <remarks>
/// Complements <see cref="OpfStructureValidator"/> — that check compares the media type with the file name's
/// EXTENSION (a cheap, purely textual heuristic); this check reads the first
/// bytes of the file and compares the media type with the signature of the ACTUAL content, so it detects e.g. a file
/// renamed from <c>.png</c> to <c>.jpg</c> with a correct (extension-consistent) but false
/// media type. Format recognition is a lightweight signature (magic bytes) detection of its own —
/// <c>Signet.Core</c> deliberately does not depend on <c>SkiaSharp</c> (that dependency exists only in
/// <c>Signet.App</c>, see <c>ImageResizer.cs</c>), and recognizing the four most common
/// raster formats in EPUB (PNG/JPEG/GIF/WebP) from the header takes a few lines, without needing
/// a full decoder.
/// </remarks>
public static class ContentTypeValidator
{
    /// <summary>Runs the mimetype-vs-content check for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (Resource resource in book.GetAllResources())
        {
            if (resource is not ImageResource || !resource.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // SVG is textual XML, not a binary bitmap — outside the scope of signature sniffing.
            if (string.Equals(resource.MediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CheckImageSignature(resource, results);
        }

        return results;
    }

    private static void CheckImageSignature(Resource resource, List<ValidationResult> results)
    {
        byte[] header;
        try
        {
            header = ReadHeader(resource.FullPath, 32);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        string? actual = DetectImageMediaType(header);
        if (actual is null)
        {
            // Signature not recognized (e.g. a format other than PNG/JPEG/GIF/WebP, or a corrupted file) —
            // that is not a false mismatch, just not enough data to compare.
            return;
        }

        if (string.Equals(actual, resource.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        results.Add(new ValidationResult(
            ValidationSeverity.Warning,
            resource.BookPath,
            -1,
            -1,
            CoreStrings.Format("Validation_MediaTypeMismatchSignature", resource.Filename, resource.MediaType, actual)));
    }

    /// <summary>Recognizes the media type from the header's byte signature; <c>null</c> when not recognized.</summary>
    private static string? DetectImageMediaType(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47
            && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
        {
            return "image/png";
        }

        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8'
            && (d[4] == '7' || d[4] == '9') && d[5] == 'a')
        {
            return "image/gif";
        }

        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F'
            && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
        {
            return "image/webp";
        }

        if (d.Length >= 2 && d[0] == 'B' && d[1] == 'M')
        {
            return "image/bmp";
        }

        return null;
    }

    private static byte[] ReadHeader(string path, int maxBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] buffer = new byte[Math.Min(maxBytes, stream.Length)];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return buffer;
    }
}
