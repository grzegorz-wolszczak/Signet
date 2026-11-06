using System;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// Tells the encoding of an XML document from its first bytes (upstream's <c>XMLEncodingSniffer</c>): a byte order mark
/// or the pattern of <c>&lt;?</c> in UTF-16 or UCS-4, EBCDIC, else the <c>encoding</c> of the XML declaration.
/// </summary>
public static class XMLEncodingSniffer
{
    /// <summary>How many bytes are looked at.</summary>
    public const int SniffLength = 256;

    private const int MinimumLength = 4;
    private const string EncodingAttribute = "encoding=";
    private const byte MaxAscii = 0x7F;

    private static readonly byte[][] s_utf16Magic = [[0xFE, 0xFF], [0xFF, 0xFE], [0, 0x3C, 0, 0x3F], [0x3C, 0, 0x3F, 0]];

    private static readonly byte[][] s_ucs4Magic =
    [
        [0, 0, 0xFE, 0xFF], [0xFF, 0xFE, 0, 0], [0, 0, 0xFF, 0xFE], [0xFE, 0xFF, 0, 0],
        [0, 0, 0, 0x3C], [0, 0, 0x3C, 0], [0, 0x3C, 0, 0], [0x3C, 0, 0, 0],
    ];

    private static readonly byte[] s_utf8Magic = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] s_ebcdicMagic = [0x4C, 0x6F, 0xA7, 0x94];

    /// <summary>The encoding of the document starting with <paramref name="bytes"/> (upper case), or <c>null</c> when it
    /// does not say (probably UTF-8).</summary>
    public static string? SniffEncoding(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> buffer = bytes[..Math.Min(bytes.Length, SniffLength)];
        if (buffer.Length < MinimumLength)
        {
            return null;
        }

        foreach (byte[] magic in s_utf16Magic)
        {
            if (buffer.StartsWith(magic))
            {
                return "UTF-16";
            }
        }

        foreach (byte[] magic in s_ucs4Magic)
        {
            if (buffer.StartsWith(magic))
            {
                return "UCS-4";
            }
        }

        if (buffer.StartsWith(s_utf8Magic))
        {
            return "UTF-8";
        }

        if (buffer.StartsWith(s_ebcdicMagic))
        {
            return "EBCDIC";
        }

        int asciiLength = 0;
        while (asciiLength < buffer.Length && buffer[asciiLength] != 0 && buffer[asciiLength] <= MaxAscii)
        {
            asciiLength++;
        }

        string header = Encoding.ASCII.GetString(buffer[..asciiLength]);
        int index = header.IndexOf(EncodingAttribute, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        index += EncodingAttribute.Length;
        if (index >= header.Length)
        {
            return null;
        }

        char quote = header[index];
        if (quote is not ('"' or '\''))
        {
            return null;
        }

        int end = header.IndexOf(quote, index + 1);
        return end < 0 ? null : header[(index + 1)..end].ToUpper(CultureInfo.InvariantCulture);
    }

    /// <summary>Whether <paramref name="bytes"/> start with the UTF-8 byte order mark.</summary>
    public static bool HasUtf8Bom(ReadOnlySpan<byte> bytes) => bytes.StartsWith(s_utf8Magic);
}
