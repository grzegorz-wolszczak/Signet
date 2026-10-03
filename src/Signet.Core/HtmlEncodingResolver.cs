using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core;

/// <summary>
/// Detects the encoding of an (X)HTML / XML file and returns the text converted to Unicode.
/// </summary>
/// <remarks>
/// Detection order: BOM (UTF-8/16/32) -&gt; the "char, 0, char, 0" heuristic (UTF-16LE without BOM)
/// -&gt; the <c>encoding=</c> attribute in the first 1024 bytes -&gt; the <c>charset=</c> attribute -&gt;
/// "is the whole file valid UTF-8" -&gt; Windows-1252 as a last resort. Names such as <c>cp1252</c> are
/// mapped to <c>windows-1252</c> etc.
/// </remarks>
public static partial class HtmlEncodingResolver
{
    private static readonly bool CodePagesRegistered = RegisterCodePages();

    [GeneratedRegex("encoding\\s*=\\s*(?:\"|')([^\"']+)(?:\"|')")]
    private static partial Regex EncodingAttribute();

    [GeneratedRegex("charset\\s*=\\s*(?:\"|')([^\"']+)(?:\"|')")]
    private static partial Regex CharsetAttribute();

    /// <summary>
    /// Reads an (X)HTML/XML file, detects the encoding and returns the text as Unicode,
    /// normalized (NFC, <c>\n</c> line endings).
    /// </summary>
    /// <exception cref="IOException">When the file cannot be read.</exception>
    public static string ReadHtmlFile(string fullFilePath)
    {
        ArgumentNullException.ThrowIfNull(fullFilePath);
        byte[] data = File.ReadAllBytes(fullFilePath);
        return Utility.ConvertLineEndingsAndNormalize(GetEncodingForHtml(data).GetString(StripBom(data)));
    }

    /// <summary>
    /// Guesses the encoding of an (X)HTML/XML byte stream. When nothing indicates otherwise — UTF-8.
    /// </summary>
    public static Encoding GetEncodingForHtml(byte[] rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        if (rawText.Length < 4)
        {
            return Utility.Utf8NoBom;
        }

        byte c1 = rawText[0];
        byte c2 = rawText[1];
        byte c3 = rawText[2];
        byte c4 = rawText[3];

        if (c1 == 0xEF && c2 == 0xBB && c3 == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (c1 == 0xFF && c2 == 0xFE && c3 == 0 && c4 == 0)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (c1 == 0 && c2 == 0 && c3 == 0xFE && c4 == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (c1 == 0xFE && c2 == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        if (c1 == 0xFF && c2 == 0xFE)
        {
            return Encoding.Unicode;
        }

        // Alternating char followed by 0 is typical of UTF-16LE without BOM.
        if (c1 != 0 && c2 == 0 && c3 != 0 && c4 == 0)
        {
            return Encoding.Unicode;
        }

        // Look for an encoding declared in the file itself (first ~1024 bytes, treated as Latin-1).
        string head = Encoding.Latin1.GetString(rawText, 0, Math.Min(1024, rawText.Length));

        Match encodingMatch = EncodingAttribute().Match(head);
        if (encodingMatch.Success && TryGetEncoding(encodingMatch.Groups[1].Value, out Encoding declared))
        {
            return declared;
        }

        Match charsetMatch = CharsetAttribute().Match(head);
        if (charsetMatch.Success && TryGetEncoding(charsetMatch.Groups[1].Value, out Encoding charset))
        {
            return charset;
        }

        if (IsValidUtf8(rawText))
        {
            return Utility.Utf8NoBom;
        }

        // Last resort: assume Windows-1252, the most common legacy web encoding.
        return TryGetEncoding("windows-1252", out Encoding fallback) ? fallback : Utility.Utf8NoBom;
    }

    /// <summary>
    /// Checks whether the whole byte sequence is valid UTF-8 (matches the pattern from
    /// https://www.w3.org/International/questions/qa-forms-utf-8).
    /// </summary>
    public static bool IsValidUtf8(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int index = 0;
        Span<byte> b = stackalloc byte[4];
        while (index < data.Length)
        {
            b.Clear();
            int available = Math.Min(4, data.Length - index);
            data.AsSpan(index, available).CopyTo(b);

            if (b[0] == 0x09 || b[0] == 0x0A || b[0] == 0x0D || (b[0] >= 0x20 && b[0] <= 0x7E))
            {
                index += 1;
            }
            else if (b[0] >= 0xC2 && b[0] <= 0xDF && b[1] >= 0x80 && b[1] <= 0xBF)
            {
                index += 2;
            }
            else if ((b[0] == 0xE0 && b[1] >= 0xA0 && b[1] <= 0xBF && b[2] >= 0x80 && b[2] <= 0xBF) ||
                     (((b[0] >= 0xE1 && b[0] <= 0xEC) || b[0] == 0xEE || b[0] == 0xEF) &&
                      b[1] >= 0x80 && b[1] <= 0xBF && b[2] >= 0x80 && b[2] <= 0xBF) ||
                     (b[0] == 0xED && b[1] >= 0x80 && b[1] <= 0x9F && b[2] >= 0x80 && b[2] <= 0xBF))
            {
                index += 3;
            }
            else if ((b[0] == 0xF0 && b[1] >= 0x90 && b[1] <= 0xBF && b[2] >= 0x80 && b[2] <= 0xBF && b[3] >= 0x80 && b[3] <= 0xBF) ||
                     (b[0] >= 0xF1 && b[0] <= 0xF3 && b[1] >= 0x80 && b[1] <= 0xBF && b[2] >= 0x80 && b[2] <= 0xBF && b[3] >= 0x80 && b[3] <= 0xBF) ||
                     (b[0] == 0xF4 && b[1] >= 0x80 && b[1] <= 0x8F && b[2] >= 0x80 && b[2] <= 0xBF && b[3] >= 0x80 && b[3] <= 0xBF))
            {
                index += 4;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Maps <c>cp125x</c> variants to the canonical <c>windows-125x</c>.</summary>
    public static string FixupCodePageMapping(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (int page = 1250; page <= 1258; page++)
        {
            string suffix = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (name.Equals("cp" + suffix, StringComparison.OrdinalIgnoreCase) ||
                name.Equals("cp-" + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return "windows-" + suffix;
            }
        }

        return name;
    }

    /// <summary>
    /// Tries to resolve an encoding name (after mapping <c>cp125x</c> -&gt; <c>windows-125x</c>),
    /// registering <see cref="CodePagesEncodingProvider"/> if needed. Returns <c>false</c>
    /// for unknown names; <paramref name="encoding"/> is then UTF-8 without BOM.
    /// </summary>
    public static bool TryGetEncoding(string name, out Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureCodePagesRegistered();
        try
        {
            encoding = Encoding.GetEncoding(FixupCodePageMapping(name.Trim()));
            return true;
        }
        catch (ArgumentException)
        {
            encoding = Utility.Utf8NoBom;
            return false;
        }
    }

    private static void EnsureCodePagesRegistered()
    {
        // Forces one-time static initialization (thread-safe, completed before the
        // first read of the field) — otherwise parallel tests could reach
        // Encoding.GetEncoding before RegisterProvider has finished.
        _ = CodePagesRegistered;
    }

    private static bool RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    private static byte[] StripBom(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return data[3..];
        }

        if (data.Length >= 4 && ((data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0) ||
                                 (data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF)))
        {
            return data[4..];
        }

        if (data.Length >= 2 && ((data[0] == 0xFE && data[1] == 0xFF) || (data[0] == 0xFF && data[1] == 0xFE)))
        {
            return data[2..];
        }

        return data;
    }
}
