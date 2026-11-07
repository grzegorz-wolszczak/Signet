using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Css;

/// <summary>An escape sequence of CSS, <c>\</c> and a character or up to six hexadecimal digits (upstream's <c>CssEscape</c>).</summary>
internal sealed class CssEscape
{
    private readonly CssReader _reader;
    private string _sequence = string.Empty;

    private CssEscape(CssReader reader)
    {
        _reader = reader;
    }

    /// <summary>The escaped character.</summary>
    public int Character { get; private set; }

    /// <summary>Reads the escape that starts at the backslash the reader is on; <c>null</c> when there is none (a line end follows the backslash).</summary>
    public static CssEscape? Create(CssReader reader, CssToken.TokenBuilder err)
    {
        CssEscape escape = new(reader);
        return escape.Read(err) ? escape : null;
    }

    /// <summary>Writes the escape to <paramref name="builder"/>: the character itself when <paramref name="asLiteral"/> matches it, else as written; returns the number of characters to read on.</summary>
    public int Render(CssToken.TokenBuilder builder, CharMatcher asLiteral)
    {
        char ch = (char)Character;
        if (asLiteral.Matches(ch))
        {
            builder.Append(ch);
        }
        else
        {
            builder.Append(_sequence);
        }

        return _sequence.Length - 1;
    }

    private bool Read(CssToken.TokenBuilder err)
    {
        StringBuilder sb = new();
        int[] eight = _reader.Peek(8);
        int first = eight[0];
        if (first == -1)
        {
            err.Error(CssErrorCode.ScannerPrematureEof, _reader);
            return false;
        }

        if (CssScanner.IsNewLine(eight) > 0)
        {
            return false;
        }

        if (CssScanner.HexChar.Matches((char)first))
        {
            bool seenSpace = false;
            foreach (int cur in eight)
            {
                if (cur == -1)
                {
                    break;
                }

                char ch = (char)cur;
                bool isHexChar = CssScanner.HexChar.Matches(ch);
                bool isSpace = CssScanner.Whitespace.Matches(ch);
                if (!isHexChar && !isSpace)
                {
                    break;
                }

                if (isHexChar && !seenSpace && sb.Length < 6)
                {
                    sb.Append(ch);
                }

                if (isSpace)
                {
                    if (!seenSpace)
                    {
                        sb.Append(ch);
                        if (cur == '\r')
                        {
                            seenSpace = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (cur == '\f')
                        {
                            sb.Append(ch);
                        }

                        break;
                    }
                }
            }

            Character = int.Parse(CssScanner.Whitespace.TrimTrailingFrom(sb.ToString()), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            sb.Insert(0, '\\');
            _sequence = sb.ToString();
        }
        else
        {
            sb.Append('\\').Append((char)eight[0]);
            _sequence = sb.ToString();
            Character = eight[0];
        }

        return true;
    }
}
