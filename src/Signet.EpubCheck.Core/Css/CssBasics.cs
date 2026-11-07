using System;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text;

namespace Signet.EpubCheck.Core.Css;

/// <summary>The characters a scanner looks for (what upstream takes from Guava's <c>CharMatcher</c>).</summary>
internal sealed class CharMatcher
{
    private readonly Func<char, bool> _predicate;

    private CharMatcher(Func<char, bool> predicate)
    {
        _predicate = predicate;
    }

    /// <summary>The characters in <paramref name="chars"/>.</summary>
    public static CharMatcher AnyOf(string chars) => new(chars.Contains);

    /// <summary>The characters from <paramref name="first"/> to <paramref name="last"/>.</summary>
    public static CharMatcher InRange(char first, char last) => new(c => c >= first && c <= last);

    /// <summary>The character <paramref name="ch"/>.</summary>
    public static CharMatcher Is(char ch) => new(c => c == ch);

    /// <summary>Whether <paramref name="c"/> is matched.</summary>
    public bool Matches(char c) => _predicate(c);

    /// <summary>The characters matched by this or by <paramref name="other"/>.</summary>
    public CharMatcher Or(CharMatcher other) => new(c => _predicate(c) || other._predicate(c));

    /// <summary>The characters this does not match.</summary>
    public CharMatcher Negate() => new(c => !_predicate(c));

    /// <summary><paramref name="text"/> without the matched characters at its end.</summary>
    public string TrimTrailingFrom(string text)
    {
        int end = text.Length;
        while (end > 0 && _predicate(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }
}

/// <summary>Where something is in a style sheet (upstream's <c>CssLocation</c>).</summary>
public sealed class CssLocation : IEquatable<CssLocation>
{
    /// <summary>The system id of a text that does not come from a file.</summary>
    public const string NoSystemId = "VIRTUAL";

    internal CssLocation(int line, int column, int charOffset, string systemId)
    {
        Line = line;
        Column = column;
        CharOffset = charOffset;
        SystemId = systemId ?? throw new ArgumentNullException(nameof(systemId));
    }

    /// <summary>The line, from 1.</summary>
    public int Line { get; }

    /// <summary>The column, from 1.</summary>
    public int Column { get; }

    /// <summary>The offset of the character.</summary>
    public int CharOffset { get; }

    /// <summary>The system id of the source.</summary>
    public string SystemId { get; }

    internal static CssLocation Create(CssReader reader) => new(reader.Line, reader.Column, reader.Offset, reader.SystemId);

    /// <inheritdoc />
    public bool Equals(CssLocation? other) =>
        other is not null && other.CharOffset == CharOffset && other.Line == Line && other.Column == Column && other.SystemId == SystemId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CssLocation);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Line, Column, CharOffset, SystemId);

    /// <inheritdoc />
    public override string ToString() => $"CssLocation{{line={Line}, col={Column}, charOffset={CharOffset}}}";
}

/// <summary>The codes of the errors of the scanner and the grammar (upstream's <c>CssExceptions.CssErrorCode</c>).</summary>
public enum CssErrorCode
{
    /// <summary>A general syntax error of a token.</summary>
    ScannerIllegalSyntax,

    /// <summary>A character that is not allowed in a token.</summary>
    ScannerIllegalChar,

    /// <summary>A character that is not allowed as the first of a token.</summary>
    ScannerIllegalFirstChar,

    /// <summary>A malformed escape.</summary>
    ScannerMalformedEscape,

    /// <summary>A malformed unicode range.</summary>
    ScannerIllegalUrange,

    /// <summary>The file ends inside a token.</summary>
    ScannerPrematureEof,

    /// <summary>The file ends inside a rule.</summary>
    GrammarPrematureEof,

    /// <summary>A token that is not allowed here.</summary>
    GrammarUnexpectedToken,

    /// <summary>A token that is not allowed here, where another was expected.</summary>
    GrammarExpectingToken,

    /// <summary>A selector that is not valid.</summary>
    GrammarInvalidSelector,
}

/// <summary>The error of the CSS scanner or grammar (upstream's <c>CssExceptions.CssException</c>).</summary>
public abstract class CssException : Exception
{
    private static readonly ResourceManager s_messages = new("Signet.EpubCheck.Core.Css.CssMessages", typeof(CssException).Assembly);

    internal CssException(CssToken? token, CssErrorCode errorCode, CssLocation location, CultureInfo? culture, params object?[] arguments)
        : base(Format(errorCode, culture, arguments))
    {
        ErrorCode = errorCode;
        Location = location ?? throw new ArgumentNullException(nameof(location));
        Token = token;
    }

    /// <summary>The code of the error.</summary>
    public CssErrorCode ErrorCode { get; }

    /// <summary>Where the error is.</summary>
    public CssLocation Location { get; }

    /// <summary>The token the error is about, if any.</summary>
    internal CssToken? Token { get; }

    /// <summary>The text of the entry <paramref name="key"/> of the messages.</summary>
    internal static string Text(string key, CultureInfo? culture) =>
        s_messages.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    private static string Format(CssErrorCode code, CultureInfo? culture, object?[] arguments)
    {
        string pattern = Text(Key(code), culture);
        for (int i = 0; i < arguments.Length; i++)
        {
            pattern = pattern.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", Convert.ToString(arguments[i], CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return pattern;
    }

    private static string Key(CssErrorCode code) => code switch
    {
        CssErrorCode.ScannerIllegalSyntax => "css.scanner.token.syntax",
        CssErrorCode.ScannerIllegalChar => "css.scanner.token.syntax.char",
        CssErrorCode.ScannerIllegalFirstChar => "css.scanner.token.syntax.firstChar",
        CssErrorCode.ScannerMalformedEscape => "css.scanner.token.syntax.escape",
        CssErrorCode.ScannerIllegalUrange => "css.scanner.token.syntax.urange",
        CssErrorCode.ScannerPrematureEof => "css.scanner.prematureEOF",
        CssErrorCode.GrammarPrematureEof => "css.grammar.prematureEOF",
        CssErrorCode.GrammarUnexpectedToken => "css.grammar.token.unexpected",
        CssErrorCode.GrammarExpectingToken => "css.grammar.token.expecting",
        CssErrorCode.GrammarInvalidSelector => "css.grammar.invalidSelector",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name}{{errorCode={ErrorCode}, location={Location}}}";
}

/// <summary>An error of the scanner.</summary>
public sealed class CssScannerException : CssException
{
    internal CssScannerException(CssErrorCode errorCode, CssLocation location, CultureInfo? culture, params object?[] arguments)
        : base(null, errorCode, location, culture, arguments)
    {
    }
}

/// <summary>An error of the grammar.</summary>
public sealed class CssGrammarException : CssException
{
    internal CssGrammarException(CssErrorCode errorCode, CssLocation location, CultureInfo? culture, params object?[] arguments)
        : base(null, errorCode, location, culture, arguments)
    {
    }
}

/// <summary>What is told about the errors of a style sheet; an error it throws stops the parse (upstream's <c>CssErrorHandler</c>).</summary>
public interface ICssErrorHandler
{
    /// <summary>An error was found.</summary>
    /// <exception cref="CssException">The parse should stop.</exception>
    void HandleError(CssException exception);
}

/// <summary>An error handler that goes on after every error (upstream's <c>ForgivingErrorHandler</c>).</summary>
public sealed class ForgivingErrorHandler : ICssErrorHandler
{
    /// <summary>The handler.</summary>
    public static ForgivingErrorHandler Instance { get; } = new();

    /// <inheritdoc />
    public void HandleError(CssException exception)
    {
    }
}

/// <summary>The input of the CSS parser: the bytes of a style sheet with their byte order mark and <c>@charset</c> (upstream's <c>CssInputStream</c> and <c>CssSource</c>).</summary>
public sealed class CssSource
{
    private const int MaxPushback = 256;
    private const int BomProbe = 4;
    private static readonly (string Name, byte[] Bom)[] s_boms =
    [
        ("UTF-32BE", [0x00, 0x00, 0xFE, 0xFF]),
        ("UTF-32LE", [0xFE, 0xFF, 0x00, 0x00]),
        ("UTF-8", [0xEF, 0xBB, 0xBF]),
        ("UTF-16BE", [0xFE, 0xFF]),
        ("UTF-16LE", [0xFF, 0xFE]),
    ];

    private readonly byte[] _content;

    /// <summary>A source made of the bytes in <paramref name="input"/>, named <paramref name="systemId"/>.</summary>
    public CssSource(string systemId, Stream input)
    {
        SystemId = systemId ?? throw new ArgumentNullException(nameof(systemId));
        ArgumentNullException.ThrowIfNull(input);
        using MemoryStream buffer = new();
        input.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        (Bom, int bomLength) = DetectBom(bytes);
        _content = bytes[bomLength..];
        string? charset = DetectCharset(_content, Bom);
        Charset = string.IsNullOrEmpty(charset) ? null : charset;
    }

    /// <summary>A source made of the text <paramref name="input"/>, named <paramref name="systemId"/>.</summary>
    public CssSource(string systemId, string input)
        : this(systemId, new MemoryStream(Encoding.UTF8.GetBytes(input ?? throw new ArgumentNullException(nameof(input)))))
    {
    }

    /// <summary>The system id.</summary>
    public string SystemId { get; }

    /// <summary>The name of the encoding the byte order mark tells, or <c>null</c>.</summary>
    public string? Bom { get; }

    /// <summary>The name of the encoding the <c>@charset</c> rule tells, or <c>null</c>.</summary>
    public string? Charset { get; }

    /// <summary>A reader of the style sheet in its encoding (the byte order mark's, else the <c>@charset</c>'s, else UTF-8).</summary>
    public TextReader NewReader()
    {
        Encoding encoding = Encoding.UTF8;
        string? name = Bom ?? Charset;
        if (name is not null)
        {
            try
            {
                encoding = Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return new StreamReader(new MemoryStream(_content), encoding, detectEncodingFromByteOrderMarks: false);
    }

    /// <inheritdoc />
    public override string ToString() => $"CssSource{{{SystemId}}}";

    private static (string? Name, int Length) DetectBom(byte[] bytes)
    {
        byte[] data = new byte[BomProbe];
        Array.Copy(bytes, data, Math.Min(bytes.Length, BomProbe));
        foreach ((string name, byte[] bom) in s_boms)
        {
            bool match = true;
            for (int i = 0; i < bom.Length; i++)
            {
                if (data[i] != bom[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return (name, bom.Length);
            }
        }

        return (null, 0);
    }

    // The encoding the '@charset "…";' rule at the start of the style sheet tells, looking at no more bytes than upstream does.
    private static string? DetectCharset(byte[] content, string? bom)
    {
        string encoding = bom ?? "UTF-8";
        int width = 1;
        if (encoding.StartsWith("UTF-16", StringComparison.Ordinal))
        {
            width = 2;
        }
        else if (encoding.StartsWith("UTF-32", StringComparison.Ordinal))
        {
            width = 4;
        }

        bool littleEndian = width != 1 && !encoding.EndsWith("BE", StringComparison.Ordinal);
        string? value = null;
        StringBuilder seen = new();
        char openQuote = '\0';
        int openQuotePos = -1;
        int position = 0;
        int consumed = 0;
        while (true)
        {
            byte[] unit = new byte[width];
            for (int i = 0; i < width; i++)
            {
                if (position >= content.Length)
                {
                    break;
                }

                unit[i] = content[position++];
                consumed++;
            }

            if (consumed >= MaxPushback)
            {
                break;
            }

            seen.Append((char)(width == 1 || littleEndian ? unit[0] : unit[width - 1]));
            char cur = seen[^1];
            if ((seen.Length == 1 && cur != '@') || ";}{".Contains(cur) || (seen.Length == 8 && seen.ToString() != "@charset"))
            {
                break;
            }

            if (openQuote == '\0' && (cur == '"' || cur == '\''))
            {
                openQuote = cur;
                openQuotePos = seen.Length;
            }
            else if (openQuote == cur)
            {
                if (cur == '"' || cur == '\'')
                {
                    value = seen.ToString(openQuotePos, seen.Length - 1 - openQuotePos);
                }

                break;
            }

            // Upstream goes on at the end of the input (and never stops on "@charset" alone); there is nothing more to read.
            if (position >= content.Length)
            {
                break;
            }
        }

        return value;
    }
}
