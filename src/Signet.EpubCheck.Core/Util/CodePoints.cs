namespace Signet.EpubCheck.Core.Util;

/// <summary>Code point predicates defined by Infra and MIME Sniffing.</summary>
public static class CodePoints
{
    public static bool IsAsciiWhitespace(int value) => value is 0x20 or 0x09 or 0x0A or 0x0C or 0x0D;

    public static bool IsAsciiDigit(int value) => value is >= '0' and <= '9';

    public static bool IsAsciiAlpha(int value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static bool IsAsciiAlphanum(int value) => IsAsciiAlpha(value) || IsAsciiDigit(value);

    public static bool IsHttpTokenCodePoint(int value) =>
        IsAsciiAlphanum(value) || value is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    public static bool IsHttpQuotedStringTokenCodePoint(int value) => value == '\t' || value is >= 0x20 and <= 0x7E or >= 0x80 and <= 0xFF;

    public static bool IsHttpWhitespace(int value) => value is ' ' or '\t' or '\n' or '\r';
}
