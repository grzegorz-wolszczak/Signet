using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Signet.Core.Misc;

/// <summary>
/// Code View (X)HTML/XML highlighter — a line-based state machine covering tags,
/// attributes, comments, DOCTYPE, the <c>&lt;style&gt;</c> block with CSS comments, entities and
/// special spaces. No UI dependencies — the view layer picks the colors.
/// </summary>
/// <remarks>
/// Spell checking is handled separately by <c>SpellCheckHighlightRenderer</c> in App, so it is
/// not done here.
/// </remarks>
public sealed class XhtmlHighlighter : ILineSyntaxHighlighter
{
    // Parser states (mutually exclusive).
    private const int StateText = -1;
    private const int StateTagStart = 1;
    private const int StateTagName = 2;
    private const int StateInsideTag = 3;
    private const int StateAttName = 4;
    private const int StateSingleQuote = 5;
    private const int StateDoubleQuote = 6;
    private const int StateAttValue = 7;
    private const int StateDoctype = 8;
    private const int StateComment = 9;
    private const int StateCssTagStart = 11;
    private const int StateCssTagName = 12;
    private const int StateCssInsideTag = 13;
    private const int StateCssAttName = 14;
    private const int StateCssSingleQuote = 15;
    private const int StateCssDoubleQuote = 16;
    private const int StateCssAttValue = 17;
    private const int StateCss = 18;
    private const int StateCssComment = 19;

    // "<\s*style[^>]*>" matched exactly at the current position.
    private static readonly Regex CssBegin = new(@"^<\s*style[^>]*>", RegexOptions.CultureInvariant);

    private const string Whitespace = " \f\t\r\n";

    // Special spaces: U+00A0, U+2000–U+200A, U+202F, U+3000.
    private static bool IsSpecialSpace(char ch) =>
        ch == ' ' || (ch >= ' ' && ch <= ' ') || ch == ' ' || ch == '　';

    /// <inheritdoc />
    public int InitialState => StateText;

    /// <inheritdoc />
    public int HighlightLine(string text, int previousState, List<SyntaxSpan>? spans)
    {
        ArgumentNullException.ThrowIfNull(text);

        int state = previousState;
        int n = text.Length;
        int pos = 0;
        int nstate = StateText;

        void SetFormat(int start, int length, SyntaxFormat format)
        {
            if (length > 0)
            {
                spans?.Add(new SyntaxSpan(start, length, format));
            }
        }

        while (pos < n)
        {
            int start;
            char ch;
            switch (state)
            {
                case StateComment:
                    start = pos;
                    nstate = state;
                    while (pos < n)
                    {
                        if (At(text, pos, "-->"))
                        {
                            pos += 3;
                            nstate = StateText;
                            break;
                        }

                        pos++;
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlComment);
                    break;

                case StateDoctype:
                    nstate = state;
                    start = pos;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == '>')
                        {
                            nstate = StateText;
                            break;
                        }
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlDoctype);
                    break;

                case StateTagStart:
                case StateCssTagStart:
                    // At '<' in e.g. "<span>foo</span>".
                    nstate = state;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == '>')
                        {
                            nstate = state == StateCssTagStart ? StateCss : StateText;
                            break;
                        }

                        if (ch != ' ')
                        {
                            pos--;
                            nstate = state == StateCssTagStart ? StateCssTagName : StateTagName;
                            break;
                        }
                    }

                    break;

                case StateTagName:
                case StateCssTagName:
                    // At 'b' in e.g. "<blockquote>foo</blockquote>".
                    nstate = state;
                    start = pos;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (Whitespace.Contains(ch, StringComparison.Ordinal))
                        {
                            pos--;
                            nstate = state == StateCssTagName ? StateCssInsideTag : StateInsideTag;
                            break;
                        }

                        if (ch == '>')
                        {
                            nstate = state == StateCssTagName ? StateCss : StateText;
                            break;
                        }
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlTagName);
                    break;

                case StateInsideTag:
                case StateCssInsideTag:
                    // After the tag name, before the tag is closed ('>').
                    nstate = state;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == '/')
                        {
                            continue;
                        }

                        if (ch == '>')
                        {
                            nstate = state == StateCssInsideTag ? StateCss : StateText;
                            SetFormat(pos - 1, 1, SyntaxFormat.XhtmlTagName);
                            break;
                        }

                        if (!Whitespace.Contains(ch, StringComparison.Ordinal))
                        {
                            pos--;
                            nstate = state == StateCssInsideTag ? StateCssAttName : StateAttName;
                            break;
                        }
                    }

                    break;

                case StateAttName:
                case StateCssAttName:
                    // At 's' in e.g. <img src=bla.png/>.
                    nstate = state;
                    start = pos;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == '=')
                        {
                            nstate = state == StateCssAttName ? StateCssAttValue : StateAttValue;
                            break;
                        }

                        if (ch == '/')
                        {
                            nstate = state == StateCssAttName ? StateCssInsideTag : StateInsideTag;
                            break;
                        }

                        if (ch == '>')
                        {
                            nstate = state == StateCssAttName ? StateCss : StateText;
                            break;
                        }
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlAttributeName);
                    break;

                case StateAttValue:
                case StateCssAttValue:
                    // After '=' in e.g. <img src=bla.png/>; look for the first non-space character.
                    nstate = state;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == '\'')
                        {
                            nstate = state == StateCssAttValue ? StateCssSingleQuote : StateSingleQuote;
                            SetFormat(pos - 1, 1, SyntaxFormat.XhtmlAttributeValue);
                            break;
                        }

                        if (ch == '"')
                        {
                            nstate = state == StateCssAttValue ? StateCssDoubleQuote : StateDoubleQuote;
                            SetFormat(pos - 1, 1, SyntaxFormat.XhtmlAttributeValue);
                            break;
                        }

                        if (ch != ' ')
                        {
                            break;
                        }
                    }

                    if (nstate is StateAttValue or StateCssAttValue)
                    {
                        // Unquoted value — up to whitespace or a tag delimiter.
                        start = pos;
                        while (pos < n)
                        {
                            ch = text[pos];
                            if (Whitespace.Contains(ch, StringComparison.Ordinal) || ch == '>' || ch == '/')
                            {
                                break;
                            }

                            pos++;
                        }

                        nstate = state == StateCssAttValue ? StateCssInsideTag : StateInsideTag;
                        SetFormat(start, pos - start, SyntaxFormat.XhtmlAttributeValue);
                    }

                    break;

                case StateSingleQuote:
                case StateCssSingleQuote:
                case StateDoubleQuote:
                case StateCssDoubleQuote:
                {
                    // After the opening quote of an attribute value.
                    char quote = state is StateSingleQuote or StateCssSingleQuote ? '\'' : '"';
                    start = pos;
                    while (pos < n)
                    {
                        ch = text[pos++];
                        if (ch == quote)
                        {
                            break;
                        }
                    }

                    // The state returns to InsideTag even when the quote was not closed on
                    // this line.
                    nstate = state is StateCssSingleQuote or StateCssDoubleQuote ? StateCssInsideTag : StateInsideTag;
                    SetFormat(start, pos - start, SyntaxFormat.XhtmlAttributeValue);
                    break;
                }

                case StateCss:
                    nstate = state;
                    start = pos;
                    while (pos < n)
                    {
                        if (At(text, pos, "/*"))
                        {
                            pos += 2;
                            nstate = StateCssComment;
                            break;
                        }

                        if (text[pos] == '<')
                        {
                            nstate = StateTagStart;
                            break;
                        }

                        pos++;
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlCss);
                    break;

                case StateCssComment:
                    nstate = state;
                    start = pos;
                    while (pos < n)
                    {
                        if (At(text, pos, "*/"))
                        {
                            pos += 2;
                            nstate = StateCss;
                            break;
                        }

                        pos++;
                    }

                    SetFormat(start, pos - start, SyntaxFormat.XhtmlCssComment);
                    break;

                default:
                    // Text state (also entities and special spaces).
                    nstate = state;
                    while (pos < n)
                    {
                        ch = text[pos];
                        if (ch == '<')
                        {
                            if (At(text, pos, "<!--"))
                            {
                                nstate = StateComment;
                            }
                            else if (At(text, pos, "<!DOCTYPE"))
                            {
                                nstate = StateDoctype;
                            }
                            else if (CssBegin.IsMatch(text.AsSpan(pos)))
                            {
                                nstate = StateCssTagStart;
                            }
                            else
                            {
                                nstate = StateTagStart;
                            }

                            break;
                        }

                        if (ch == '&')
                        {
                            start = pos;
                            while (pos < n && text[pos] != ';')
                            {
                                pos++;
                            }

                            SetFormat(start, pos - start, SyntaxFormat.XhtmlEntity);
                        }
                        else if (IsSpecialSpace(ch))
                        {
                            SetFormat(pos, 1, SyntaxFormat.XhtmlSpecialSpace);
                            pos++;
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    break;
            }

            state = nstate;
        }

        // An empty line yields nstate == -1 (the loop is never entered).
        return nstate;
    }

    // Whether text contains literal starting at pos.
    private static bool At(string text, int pos, string literal) =>
        pos + literal.Length <= text.Length &&
        string.CompareOrdinal(text, pos, literal, 0, literal.Length) == 0;
}
