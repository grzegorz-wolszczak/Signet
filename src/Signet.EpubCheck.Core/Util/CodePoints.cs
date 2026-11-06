namespace Signet.EpubCheck.Core.Util;

/// <summary>
/// The code point classes of the WHATWG Infra and MIME Sniffing standards (upstream's
/// <c>org.w3c.epubcheck.util.infra.CodePoints</c>).
/// </summary>
public static class CodePoints
{
    /// <summary>ASCII whitespace: TAB, LF, FF, CR or SPACE.</summary>
    public static bool IsAsciiWhitespace(int c) => c is 0x0020 or 0x0009 or 0x000A or 0x000C or 0x000D;

    /// <summary>An ASCII digit, <c>0</c> to <c>9</c>.</summary>
    public static bool IsAsciiDigit(int c) => c is >= 0x0030 and <= 0x0039;

    /// <summary>An ASCII letter, <c>A</c> to <c>Z</c> or <c>a</c> to <c>z</c>.</summary>
    public static bool IsAsciiAlpha(int c) => c is >= 0x0041 and <= 0x005A or >= 0x0061 and <= 0x007A;

    /// <summary>An ASCII letter or digit.</summary>
    public static bool IsAsciiAlphanum(int c) => IsAsciiAlpha(c) || IsAsciiDigit(c);

    /// <summary>A code point of an HTTP token: an ASCII letter or digit, or one of <c>!#$%&amp;'*+-.^_`|~</c>.</summary>
    public static bool IsHttpTokenCodePoint(int c) =>
        IsAsciiAlphanum(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    /// <summary>A code point of an HTTP quoted string: TAB, SPACE to <c>~</c>, or U+0080 to U+00FF.</summary>
    public static bool IsHttpQuotedStringTokenCodePoint(int c) => c == 0x0009 || c is >= 0x0020 and <= 0x007E or >= 0x0080 and <= 0x00FF;

    /// <summary>HTTP whitespace: TAB, LF, CR or SPACE.</summary>
    public static bool IsHttpWhitespace(int c) => c is 0x0020 or 0x0009 or 0x000A or 0x000D;
}
