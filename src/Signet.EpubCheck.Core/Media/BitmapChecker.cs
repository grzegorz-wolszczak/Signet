using System;
using System.IO;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;

namespace Signet.EpubCheck.Core.Media;

/// <summary>
/// Checks a GIF, JPEG or PNG image (upstream's <c>BitmapChecker</c>): the header must match the declared media type
/// (<c>MED-004</c>, <c>OPF-029</c>), the format found in the file must be the one the extension tells (<c>PKG-022</c>), the
/// image must be readable (<c>PKG-021</c>), and it should not be larger than 3840 by 2160 pixels (<c>OPF-051</c>) or 4 MB
/// (<c>OPF-057</c>).
/// </summary>
/// <remarks>
/// Upstream reads the image with Java's ImageIO, which knows JPEG, PNG, GIF, BMP and WBMP; the port reads the sizes from the
/// headers of those formats it can tell by their first bytes (JPEG, PNG, GIF, BMP) without a native dependency.
/// </remarks>
public sealed class BitmapChecker : PublicationResourceChecker
{
    private const int HeightMax = 2 * 1080;
    private const int WidthMax = 2 * 1920;
    private const long ImageSizeMax = 4L * 1024 * 1024;
    private const int HeaderLength = 4;

    // The file extensions of ImageIO's readers (they are matched as written, in lower or upper case).
    private static readonly (string Format, string[] Suffixes)[] s_formats =
    [
        ("JPEG", ["jpg", "JPG", "jpeg", "JPEG"]),
        ("png", ["png", "PNG"]),
        ("gif", ["gif", "GIF"]),
        ("bmp", ["bmp", "BMP"]),
        ("wbmp", ["wbmp", "WBMP"]),
    ];

    /// <summary>A checker of the image of <paramref name="context"/>.</summary>
    public BitmapChecker(ValidationContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        byte[] bytes;
        try
        {
            using Stream stream = Context.Resources.OpenStream(Context.Url);
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (IOException)
        {
            Report.Message(MessageId.PKG_021, Context.Location());
            return false;
        }

        if (bytes.Length < HeaderLength)
        {
            Report.Message(MessageId.MED_004, Context.Location());
        }
        else
        {
            CheckHeader(bytes);
        }

        CheckImageDimensions(bytes);
        return true;
    }

    private void CheckHeader(byte[] header)
    {
        string mimeType = Context.MimeType;
        bool passed = mimeType switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8,
            "image/gif" => header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8',
            "image/png" => header[0] == 0x89 && header[1] == 'P' && header[2] == 'N' && header[3] == 'G',
            _ => true,
        };
        if (!passed)
        {
            Report.Message(MessageId.OPF_029, Context.Location(), Context.Path, mimeType);
        }
    }

    private void CheckImageDimensions(byte[] bytes)
    {
        try
        {
            if (GetImageSizes(bytes) is { } sizes)
            {
                if (sizes.Height >= HeightMax || sizes.Width >= WidthMax)
                {
                    Report.Message(MessageId.OPF_051, Context.Location());
                }

                if (sizes.Length >= ImageSizeMax)
                {
                    Report.Message(MessageId.OPF_057, Context.Location());
                }
            }
        }
        catch (IOException)
        {
            Report.Message(MessageId.PKG_021, Context.Location());
        }
    }

    // The size of the image, or null when a message was reported.
    private ImageHeuristics? GetImageSizes(byte[] bytes)
    {
        string path = Context.Url.Path ?? string.Empty;
        int position = path.LastIndexOf('.');
        if (position == -1)
        {
            throw new IOException("No extension for file: " + Context.Url);
        }

        string suffix = path[(position + 1)..];

        // the format by the content and by the extension
        string? formatFromContent = ImageFormats.Detect(bytes);
        string? formatFromSuffix = null;
        foreach ((string format, string[] suffixes) in s_formats)
        {
            if (formatFromContent == format && Array.IndexOf(suffixes, suffix) >= 0)
            {
                formatFromSuffix = format;
                break;
            }
        }

        if (formatFromSuffix is not null)
        {
            // the format and the extension match: read the image
            try
            {
                (int width, int height) = ImageFormats.ReadSize(formatFromSuffix, bytes);
                return new ImageHeuristics(width, height, bytes.Length);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                Report.Message(MessageId.PKG_021, Context.Location());
                return null;
            }
        }

        if (formatFromContent is not null && IsKnownSuffix(suffix))
        {
            // the format and the extension differ
            Report.Message(MessageId.PKG_022, Context.Location(), formatFromContent, suffix);
            return null;
        }

        // the format could not be determined (or the extension is not one of the readers')
        throw new IOException("Not a known image file: " + Context.Url);
    }

    private static bool IsKnownSuffix(string suffix)
    {
        foreach ((_, string[] suffixes) in s_formats)
        {
            if (Array.IndexOf(suffixes, suffix) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct ImageHeuristics(int Width, int Height, long Length);
}

/// <summary>The formats of images the port reads the size of, by their first bytes (what Java's ImageIO readers recognise).</summary>
internal static class ImageFormats
{
    private static readonly byte[] s_pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The ImageIO name of the format of <paramref name="bytes"/>, or <c>null</c> when it is not one the port knows.</summary>
    public static string? Detect(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return "JPEG";
        }

        if (bytes.Length >= s_pngSignature.Length && bytes.AsSpan(0, s_pngSignature.Length).SequenceEqual(s_pngSignature))
        {
            return "png";
        }

        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8' && bytes[4] is (byte)'7' or (byte)'9' && bytes[5] == 'a')
        {
            return "gif";
        }

        return bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M' ? "bmp" : null;
    }

    /// <summary>The width and height of the image.</summary>
    /// <exception cref="InvalidDataException">The header is truncated or wrong.</exception>
    public static (int Width, int Height) ReadSize(string format, byte[] bytes) => format switch
    {
        "png" => ReadPng(bytes),
        "gif" => ReadGif(bytes),
        "bmp" => ReadBmp(bytes),
        "JPEG" => ReadJpeg(bytes),
        _ => throw new InvalidDataException("Unsupported format " + format),
    };

    private static (int, int) ReadPng(byte[] bytes)
    {
        // signature, then the IHDR chunk: length, "IHDR", width, height
        if (bytes.Length < 24 || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
        {
            throw new InvalidDataException("No IHDR chunk");
        }

        return (BigEndian32(bytes, 16), BigEndian32(bytes, 20));
    }

    private static (int, int) ReadGif(byte[] bytes)
    {
        // The size of the first image (ImageIO reports it, not that of the logical screen).
        if (bytes.Length < 13)
        {
            throw new InvalidDataException("Truncated GIF header");
        }

        int index = 13;
        if ((bytes[10] & 0x80) != 0)
        {
            index += 3 * (1 << ((bytes[10] & 0x07) + 1));
        }

        while (index < bytes.Length)
        {
            switch (bytes[index])
            {
                case 0x21: // an extension: skip its sub-blocks
                    index += 2;
                    while (index < bytes.Length && bytes[index] != 0)
                    {
                        index += bytes[index] + 1;
                    }

                    index++;
                    break;
                case 0x2C: // the image descriptor
                    if (index + 9 > bytes.Length)
                    {
                        throw new InvalidDataException("Truncated image descriptor");
                    }

                    return (bytes[index + 5] | (bytes[index + 6] << 8), bytes[index + 7] | (bytes[index + 8] << 8));
                default:
                    throw new InvalidDataException("No image in the GIF");
            }
        }

        throw new InvalidDataException("No image in the GIF");
    }

    private static (int, int) ReadBmp(byte[] bytes)
    {
        if (bytes.Length < 26)
        {
            throw new InvalidDataException("Truncated BMP header");
        }

        int headerSize = bytes[14] | (bytes[15] << 8) | (bytes[16] << 16) | (bytes[17] << 24);
        if (headerSize == 12)
        {
            return (bytes[18] | (bytes[19] << 8), bytes[20] | (bytes[21] << 8));
        }

        int width = bytes[18] | (bytes[19] << 8) | (bytes[20] << 16) | (bytes[21] << 24);
        int height = bytes[22] | (bytes[23] << 8) | (bytes[24] << 16) | (bytes[25] << 24);
        return (Math.Abs(width), Math.Abs(height));
    }

    private static (int, int) ReadJpeg(byte[] bytes)
    {
        int index = 2;
        while (index + 4 <= bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                throw new InvalidDataException("Marker expected");
            }

            int marker = bytes[index + 1];
            if (marker == 0xFF)
            {
                index++; // padding
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                index += 2; // a marker without a length
                continue;
            }

            int length = (bytes[index + 2] << 8) | bytes[index + 3];
            bool startOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (startOfFrame)
            {
                if (index + 9 > bytes.Length)
                {
                    throw new InvalidDataException("Truncated frame header");
                }

                int height = (bytes[index + 5] << 8) | bytes[index + 6];
                int width = (bytes[index + 7] << 8) | bytes[index + 8];
                return (width, height);
            }

            index += 2 + length;
        }

        throw new InvalidDataException("No frame header in the JPEG");
    }

    private static int BigEndian32(byte[] bytes, int index) =>
        (bytes[index] << 24) | (bytes[index + 1] << 16) | (bytes[index + 2] << 8) | bytes[index + 3];
}
