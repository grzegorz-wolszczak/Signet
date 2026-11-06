using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Signet.EpubCheck.Core.Galimatias;

/// <summary>The helpers of galimatias' <c>URLUtils</c>: code point classes, percent coding, IDNA, relative schemes.</summary>
public static class UrlUtils
{
    private const string HexDigits = "0123456789ABCDEF";
    private const int HexLetterValueOffset = 10; // 'A' is 10
    private const int NibbleBits = 4;
    private const int NibbleMask = 0x0F;
    private const char PercentSign = '%';

    // The schemes of the 2014 URL standard galimatias implements, with their default ports (null: none).
    private static readonly (string Scheme, int? DefaultPort)[] s_relativeSchemes =
    [
        ("ftp", 21),
        ("file", null),
        ("gopher", 70),
        ("http", 80),
        ("https", 443),
        ("ws", 80),
        ("wss", 443),
    ];

    // .NET's IDNA (UTS #46); upstream uses ICU4J's IDNA.getUTS46Instance(IDNA.DEFAULT).
    private static readonly IdnMapping s_idn = new() { AllowUnassigned = false, UseStd3AsciiRules = false };

    /// <summary>Percent-decodes <paramref name="input"/>: the escapes become bytes, the result is read as UTF-8.</summary>
    public static string PercentDecode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            return input;
        }

        using MemoryStream bytes = new();
        Span<byte> encoded = stackalloc byte[4];
        int index = 0;
        while (index < input.Length)
        {
            if (input[index] == PercentSign && input.Length > index + 2 && IsAsciiHexDigit(input[index + 1]) && IsAsciiHexDigit(input[index + 2]))
            {
                bytes.WriteByte((byte)HexToInt(input[index + 1], input[index + 2]));
                index += 3;
                continue;
            }

            int codePoint = CodePointAt(input, index);
            int length = codePoint > char.MaxValue ? 2 : 1;
            if (codePoint <= sbyte.MaxValue)
            {
                bytes.WriteByte((byte)codePoint);
            }
            else
            {
                int written = Encoding.UTF8.GetBytes(input.AsSpan(index, length), encoded);
                bytes.Write(encoded[..written]);
            }

            index += length;
        }

        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    /// <summary>The ASCII form of a domain (UTS #46 <c>ToASCII</c>, hyphen rules not checked, as galimatias).</summary>
    /// <exception cref="GalimatiasParseException">The domain is not valid for IDNA.</exception>
    public static string DomainToAscii(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        // For ASCII without an "xn--" label, UTS #46 (no STD3 rules) only lower-cases. .NET's IDNA would also reject
        // labels that start or end with a hyphen, which galimatias lets through.
        if (IsPlainAscii(domain))
        {
            CheckAsciiLabels(domain);
            return domain.ToLowerInvariant();
        }

        try
        {
            return s_idn.GetAscii(ApplyTransitionalMapping(domain));
        }
        catch (ArgumentException ex)
        {
            throw new GalimatiasParseException("A label or domain name contains disallowed characters.", ex);
        }
    }

    /// <summary>The Unicode form of an ASCII domain (UTS #46 <c>ToUnicode</c>).</summary>
    /// <exception cref="GalimatiasParseException">The domain is not valid for IDNA.</exception>
    public static string DomainToUnicode(string asciiDomain)
    {
        ArgumentNullException.ThrowIfNull(asciiDomain);
        if (IsPlainAscii(asciiDomain))
        {
            return asciiDomain.ToLowerInvariant();
        }

        try
        {
            return s_idn.GetUnicode(asciiDomain);
        }
        catch (ArgumentException ex)
        {
            throw new GalimatiasParseException("An ACE label does not contain a valid label string.", ex);
        }
    }

    /// <summary>An ASCII hexadecimal digit.</summary>
    public static bool IsAsciiHexDigit(int c) => c is >= 'A' and <= 'F' or >= 'a' and <= 'f' || IsAsciiDigit(c);

    /// <summary>An ASCII digit.</summary>
    public static bool IsAsciiDigit(int c) => c is >= '0' and <= '9';

    /// <summary>An ASCII letter.</summary>
    public static bool IsAsciiAlpha(int c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>An ASCII letter or digit.</summary>
    public static bool IsAsciiAlphanumeric(int c) => IsAsciiAlpha(c) || IsAsciiDigit(c);

    /// <summary>A URL code point of the URL standard (2014): ASCII letters, digits and punctuation, and most of the rest of Unicode.</summary>
    public static bool IsUrlCodePoint(int c) =>
        IsAsciiAlphanumeric(c)
        || c is '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/' or ':' or ';' or '=' or '?' or '@' or '_' or '~'
        || (c >= UrlCodePointRanges.BmpFirst && c <= UrlCodePointRanges.BmpBeforeSurrogates)
        || (c >= UrlCodePointRanges.BmpAfterSurrogates && c <= UrlCodePointRanges.BmpBeforeNonCharacters)
        || (c >= UrlCodePointRanges.BmpAfterNonCharacters && c <= UrlCodePointRanges.BmpBeforeSpecials)
        || (c >= UrlCodePointRanges.SupplementaryFirst && c <= UrlCodePointRanges.SupplementaryLast
            && (c & UrlCodePointRanges.PlaneOffsetMask) <= UrlCodePointRanges.LastCodePointOfPlane);

    /// <summary>The value of two hexadecimal digits.</summary>
    public static int HexToInt(char c1, char c2) => (HexValue(c1) << NibbleBits) | HexValue(c2);

    /// <summary>Appends the escape of <paramref name="b"/> (upper-case hexadecimal).</summary>
    public static void PercentEncode(byte b, StringBuilder buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Append(PercentSign).Append(HexDigits[b >> NibbleBits]).Append(HexDigits[b & NibbleMask]);
    }

    /// <summary>Whether <paramref name="scheme"/> is one of the relative (hierarchical) schemes: ftp, file, gopher, http, https, ws, wss.</summary>
    public static bool IsRelativeScheme(string? scheme) => Array.Exists(s_relativeSchemes, entry => entry.Scheme == scheme);

    /// <summary>The default port of <paramref name="scheme"/>, or <c>null</c>.</summary>
    public static int? GetDefaultPortForScheme(string? scheme) => Array.Find(s_relativeSchemes, entry => entry.Scheme == scheme).DefaultPort;

    /// <summary>Java's <c>String.codePointAt</c>: a surrogate pair is one code point, a lone surrogate is itself.</summary>
    internal static int CodePointAt(string value, int index) =>
        char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])
            ? char.ConvertToUtf32(value[index], value[index + 1])
            : value[index];

    /// <summary>Java's <c>Character.isWhitespace</c>: space separators other than the no-break ones, and TAB to CR, FS to US.</summary>
    internal static bool IsJavaWhitespace(int c)
    {
        if (c is >= '\t' and <= '\r' or >= JavaWhitespace.FileSeparator and <= JavaWhitespace.UnitSeparator)
        {
            return true;
        }

        if (c > char.MaxValue || c is JavaWhitespace.NoBreakSpace or JavaWhitespace.FigureSpace or JavaWhitespace.NarrowNoBreakSpace)
        {
            return false;
        }

        return char.GetUnicodeCategory((char)c) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }

    private static int HexValue(char c) => c <= '9' ? c - '0' : (char.ToUpperInvariant(c) - 'A') + HexLetterValueOffset;

    // ICU's IDNA.DEFAULT is transitional processing: the four deviation characters are mapped (or removed) before
    // ToASCII. .NET's IdnMapping is nontransitional, so they are mapped here.
    private static string ApplyTransitionalMapping(string domain) => domain
        .Replace("\u00DF", "ss", StringComparison.Ordinal) // LATIN SMALL LETTER SHARP S
        .Replace("\u1E9E", "ss", StringComparison.Ordinal) // LATIN CAPITAL LETTER SHARP S
        .Replace("\u03C2", "\u03C3", StringComparison.Ordinal) // GREEK SMALL LETTER FINAL SIGMA
        .Replace("\u200C", string.Empty, StringComparison.Ordinal) // ZERO WIDTH NON-JOINER
        .Replace("\u200D", string.Empty, StringComparison.Ordinal); // ZERO WIDTH JOINER

    // The length and empty-label errors ICU reports for an ASCII name (the other UTS #46 checks change nothing there).
    private static void CheckAsciiLabels(string domain)
    {
        string name = domain.EndsWith('.') ? domain[..^1] : domain;
        if (name.Length > IdnaLimits.MaxDomainLength)
        {
            throw new GalimatiasParseException("A domain name is longer than 255 bytes in its storage form.");
        }

        foreach (string label in name.Split('.'))
        {
            if (label.Length == 0)
            {
                throw new GalimatiasParseException("A non-final domain name label (or the whole domain name) is empty.");
            }

            if (label.Length > IdnaLimits.MaxLabelLength)
            {
                throw new GalimatiasParseException("A domain name label is longer than 63 bytes.");
            }
        }
    }

    private static bool IsPlainAscii(string domain)
    {
        foreach (char c in domain)
        {
            if (c > sbyte.MaxValue)
            {
                return false;
            }
        }

        return !domain.StartsWith(Punycode.AcePrefix, StringComparison.OrdinalIgnoreCase)
            && !domain.Contains("." + Punycode.AcePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static class UrlCodePointRanges
    {
        public const int BmpFirst = 0x00A0;
        public const int BmpBeforeSurrogates = 0xD7FF;
        public const int BmpAfterSurrogates = 0xE000;
        public const int BmpBeforeNonCharacters = 0xFDCF;
        public const int BmpAfterNonCharacters = 0xFDF0;
        public const int BmpBeforeSpecials = 0xFFEF;
        public const int SupplementaryFirst = 0x10000;
        public const int SupplementaryLast = 0x10FFFD;

        // In every supplementary plane, U+x0000 to U+xFFFD (the last two are non characters).
        public const int PlaneOffsetMask = 0xFFFF;
        public const int LastCodePointOfPlane = 0xFFFD;
    }

    private static class JavaWhitespace
    {
        public const int FileSeparator = 0x1C;
        public const int UnitSeparator = 0x1F;
        public const int NoBreakSpace = 0x00A0;
        public const int FigureSpace = 0x2007;
        public const int NarrowNoBreakSpace = 0x202F;
    }

    private static class IdnaLimits
    {
        public const int MaxDomainLength = 253; // without the trailing dot
        public const int MaxLabelLength = 63;
    }

    private static class Punycode
    {
        public const string AcePrefix = "xn--";
    }
}
