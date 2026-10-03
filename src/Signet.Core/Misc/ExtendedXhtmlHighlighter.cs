using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Signet.Core.Misc;

/// <summary>The parser state at a line boundary in <see cref="ExtendedXhtmlHighlighter"/>.</summary>
public enum XhtmlParseState
{
    /// <summary>Text between tags (<c>NORMAL</c>).</summary>
    Normal,

    /// <summary>Inside an opening tag (<c>IN_OPENING_TAG</c>).</summary>
    InOpeningTag,

    /// <summary>Inside a closing tag (<c>IN_CLOSING_TAG</c>).</summary>
    InClosingTag,

    /// <summary>A comment <c>&lt;!-- --&gt;</c>.</summary>
    InComment,

    /// <summary>A processing instruction <c>&lt;? ?&gt;</c>.</summary>
    InProcessingInstruction,

    /// <summary>A <c>&lt;!DOCTYPE&gt;</c> declaration.</summary>
    InDoctype,

    /// <summary>A <c>&lt;![CDATA[ ]]&gt;</c> section (accepted, not flagged as an error).</summary>
    InCdataSection,

    /// <summary>After an attribute name (<c>ATTRIBUTE_NAME</c>).</summary>
    AttributeName,

    /// <summary>After <c>=</c> (<c>ATTRIBUTE_VALUE</c>).</summary>
    AttributeValue,

    /// <summary>A single-quoted value (<c>SQ_VAL</c>).</summary>
    SingleQuotedValue,

    /// <summary>A double-quoted value (<c>DQ_VAL</c>).</summary>
    DoubleQuotedValue,

    /// <summary>The content of <c>&lt;title&gt;</c>, <c>&lt;script&gt;</c> and other HTML "CDATA" tags (<c>CDATA</c>).</summary>
    CdataContent,

    /// <summary>The content of <c>&lt;style&gt;</c> — highlighted as CSS (<c>CSS</c>).</summary>
    Css,
}

/// <summary>
/// An open tag on the line state stack (immutable, shared between lines).
/// <see cref="IsBold"/>/<see cref="IsItalic"/> also cover the tags lower on the stack.
/// </summary>
/// <param name="Name">The tag's local name (lowercase, without a prefix).</param>
/// <param name="Parent">The tag lower on the stack.</param>
public sealed record OpenTag(string Name, OpenTag? Parent)
{
    /// <summary>Whether the text is bold (this or a lower tag is <c>b</c>/<c>strong</c>/<c>h1</c>–<c>h6</c>).</summary>
    public bool IsBold { get; } = Name is "b" or "strong" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" || (Parent?.IsBold ?? false);

    /// <summary>Whether the text is italic (this or a lower tag is <c>i</c>/<c>em</c>).</summary>
    public bool IsItalic { get; } = Name is "i" or "em" || (Parent?.IsItalic ?? false);
}

/// <summary>
/// The line state of <see cref="ExtendedXhtmlHighlighter"/>: the parser state,
/// the stack of open tags, the tag being defined, the current attribute and the state of the
/// nested CSS highlighter. Value equality (a record).
/// </summary>
public sealed record ExtendedXhtmlState(
    XhtmlParseState Parse,
    OpenTag? Tags,
    string? TagBeingDefined,
    string? AttributeName,
    int CssState)
{
    /// <summary>The state before the first line of the document.</summary>
    public static ExtendedXhtmlState Initial { get; } = new(XhtmlParseState.Normal, null, null, null, -1);
}

/// <summary>
/// Extended (X)HTML/XML highlighting: a state machine with CSS coloring inside <c>&lt;style&gt;</c>
/// (<see cref="ExtendedCssHighlighter"/>), syntax errors with a hint, links in <c>href</c>/<c>src</c>
/// (a broken link — a wavy line), a separate color for namespace prefixes, bold/italic text
/// in <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/headings and a bold <c>&lt;title&gt;</c>. Colors match
/// the base highlighter (<see cref="XhtmlHighlighter"/>); new elements get new formats.
/// </summary>
/// <remarks>
/// Spellchecking stays in a separate renderer; <c>&lt;![CDATA[</c> sections are not an error; a missing
/// attribute value before <c>&gt;</c> does not "eat" the <c>&gt;</c>; closing <c>&lt;style&gt;</c>/<c>&lt;title&gt;</c>
/// pops the tag off the stack; special spaces match the base highlighter (no non-breaking hyphens or dashes).
/// </remarks>
public sealed partial class ExtendedXhtmlHighlighter : ILineSyntaxHighlighter<ExtendedXhtmlState>
{
    private static readonly HashSet<string> CdataTags = new(StringComparer.Ordinal)
    {
        "title", "textarea", "style", "script", "xmp", "iframe", "noembed", "noframes", "noscript",
    };

    private static readonly HashSet<string> LinkAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "poster", "xlink:href",
    };

    private readonly bool _isXml;
    private readonly Func<string, bool>? _linkExists;
    private readonly ExtendedCssHighlighter _css;
    private readonly List<SyntaxSpan> _cssSpans = new();

    /// <summary>Creates the highlighter.</summary>
    /// <param name="isXml">
    /// XML mode (OPF, NCX, SVG…): without HTML "CDATA" tags (<c>style</c>, <c>title</c>…) and without
    /// bold/italic text.
    /// </param>
    /// <param name="linkExists">
    /// Checks a link target (<c>href</c>/<c>src</c>/<c>url()</c>): <c>true</c> when the file is in the book
    /// or the link is external. <c>null</c> — targets are not checked.
    /// </param>
    public ExtendedXhtmlHighlighter(bool isXml = false, Func<string, bool>? linkExists = null)
    {
        _isXml = isXml;
        _linkExists = linkExists;
        _css = new ExtendedCssHighlighter(linkExists);
    }

    /// <inheritdoc />
    public ExtendedXhtmlState InitialState => ExtendedXhtmlState.Initial;

    /// <inheritdoc />
    public ExtendedXhtmlState HighlightLine(string text, ExtendedXhtmlState previousState, List<SyntaxSpan>? spans)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(previousState);

        var line = new LineParser(this, text, previousState, spans);
        line.Run();
        return line.EndState();
    }

    // A pass over a single line — one function per state; each advances the position.
    private sealed class LineParser
    {
        private readonly ExtendedXhtmlHighlighter _owner;
        private readonly string _text;
        private readonly List<SyntaxSpan>? _spans;
        private XhtmlParseState _parse;
        private OpenTag? _tags;
        private string? _tagBeingDefined;
        private string? _attributeName;
        private int _cssState;
        private int _pos;

        public LineParser(ExtendedXhtmlHighlighter owner, string text, ExtendedXhtmlState state, List<SyntaxSpan>? spans)
        {
            _owner = owner;
            _text = text;
            _spans = spans;
            _parse = state.Parse;
            _tags = state.Tags;
            _tagBeingDefined = state.TagBeingDefined;
            _attributeName = state.AttributeName;
            _cssState = state.CssState;
        }

        public ExtendedXhtmlState EndState() => new(_parse, _tags, _tagBeingDefined, _attributeName, _cssState);

        public void Run()
        {
            int stalled = 0;
            while (_pos < _text.Length)
            {
                int before = _pos;
                Step();
                // A safeguard: zero-length steps (a state change) are
                // allowed, but not indefinitely.
                stalled = _pos == before ? stalled + 1 : 0;
                if (stalled > 4)
                {
                    break;
                }
            }

            // An empty line in <style> still goes through the CSS highlighter (state -1 stays -1).
            if (_text.Length == 0 && _parse == XhtmlParseState.Css)
            {
                _cssState = _owner._css.HighlightLine(string.Empty, _cssState, null);
            }
        }

        private void Step()
        {
            switch (_parse)
            {
                case XhtmlParseState.Normal:
                    Normal();
                    break;
                case XhtmlParseState.InOpeningTag:
                    OpeningTag();
                    break;
                case XhtmlParseState.InClosingTag:
                    ClosingTag();
                    break;
                case XhtmlParseState.AttributeName:
                    AfterAttributeName();
                    break;
                case XhtmlParseState.AttributeValue:
                    AfterEquals();
                    break;
                case XhtmlParseState.SingleQuotedValue:
                case XhtmlParseState.DoubleQuotedValue:
                    QuotedValue();
                    break;
                case XhtmlParseState.CdataContent:
                    CdataContent();
                    break;
                case XhtmlParseState.Css:
                    Css();
                    break;
                default:
                    InComment();
                    break;
            }
        }

        private void Emit(int length, SyntaxFormat? format, SyntaxIssue issue = SyntaxIssue.None)
        {
            if (length > 0 && format is { } f && _spans is not null)
            {
                _spans.Add(new SyntaxSpan(_pos, length, f, issue));
            }

            _pos += Math.Max(length, 0);
        }

        private void EmitError(int length, SyntaxIssue issue) => Emit(length, SyntaxFormat.SyntaxError, issue);

        private bool At(string s) => string.CompareOrdinal(_text, _pos, s, 0, s.Length) == 0;

        // ---- NORMAL ----
        private void Normal()
        {
            char ch = _text[_pos];
            if (ch == '<')
            {
                if (At("<!--"))
                {
                    _parse = XhtmlParseState.InComment;
                    Emit(4, SyntaxFormat.XhtmlComment);
                    return;
                }

                if (At("<![CDATA["))
                {
                    _parse = XhtmlParseState.InCdataSection;
                    Emit(9, SyntaxFormat.XhtmlDoctype);
                    return;
                }

                if (At("<?"))
                {
                    _parse = XhtmlParseState.InProcessingInstruction;
                    Emit(2, SyntaxFormat.XhtmlDoctype);
                    return;
                }

                if (At("<!") && _text.AsSpan(_pos + 2).TrimStart().StartsWith("doctype", StringComparison.OrdinalIgnoreCase))
                {
                    _parse = XhtmlParseState.InDoctype;
                    Emit(2, SyntaxFormat.XhtmlDoctype);
                    return;
                }

                Match m = TagNameRegex().Match(_text, _pos + 1);
                if (!m.Success || m.Index != _pos + 1)
                {
                    EmitError(1, SyntaxIssue.UnescapedLessThan);
                    return;
                }

                string tagName = m.Value;
                bool closing = tagName.StartsWith('/');
                if (closing)
                {
                    tagName = tagName[1..];
                }

                int colon = tagName.IndexOf(':', StringComparison.Ordinal);
                string prefix = colon >= 0 ? tagName[..colon] : string.Empty;
                string name = colon >= 0 ? tagName[(colon + 1)..] : tagName;
                if (prefix.Length > 0 && name.Length == 0)
                {
                    EmitError(m.Length + 1, SyntaxIssue.PrefixOnlyTagName);
                    return;
                }

                Emit(closing ? 2 : 1, SyntaxFormat.XhtmlTagName);
                if (prefix.Length > 0)
                {
                    Emit(prefix.Length + 1, SyntaxFormat.XhtmlNamespacePrefix);
                }

                Emit(name.Length, SyntaxFormat.XhtmlTagName);
                string localName = name.ToLowerInvariant();
                if (closing)
                {
                    _parse = XhtmlParseState.InClosingTag;
                    CloseTag(localName);
                }
                else
                {
                    _parse = XhtmlParseState.InOpeningTag;
                    _tagBeingDefined = localName;
                }

                return;
            }

            if (ch == '&')
            {
                Match m = EntityRegex().Match(_text, _pos);
                if (m.Success && m.Index == _pos)
                {
                    Emit(m.Length, SyntaxFormat.XhtmlEntity);
                }
                else
                {
                    EmitError(1, SyntaxIssue.UnescapedAmpersand);
                }

                return;
            }

            if (ch == '>')
            {
                EmitError(1, SyntaxIssue.UnescapedGreaterThan);
                return;
            }

            int end = _text.IndexOfAny(['<', '>', '&'], _pos);
            Text(end < 0 ? _text.Length : end);
        }

        // Plain text: special spaces + bold/italic per the stack.
        private void Text(int end)
        {
            SyntaxFormat? style = _owner._isXml ? null : TextStyle(_tags?.IsBold ?? false, _tags?.IsItalic ?? false);
            while (_pos < end)
            {
                int run = _pos;
                while (run < end && !IsSpecialSpace(_text[run]))
                {
                    run++;
                }

                Emit(run - _pos, style);
                int special = _pos;
                while (special < end && IsSpecialSpace(_text[special]))
                {
                    special++;
                }

                Emit(special - _pos, SyntaxFormat.XhtmlSpecialSpace);
            }
        }

        private static SyntaxFormat? TextStyle(bool bold, bool italic) => (bold, italic) switch
        {
            (true, true) => SyntaxFormat.XhtmlBoldItalicText,
            (true, false) => SyntaxFormat.XhtmlBoldText,
            (false, true) => SyntaxFormat.XhtmlItalicText,
            _ => null,
        };

        // The special spaces: U+00A0, U+2000–U+200A, U+202F, U+3000.
        private static bool IsSpecialSpace(char ch) =>
            ch == ' ' || (ch >= ' ' && ch <= ' ') || ch == ' ' || ch == '　';

        // ---- IN_OPENING_TAG ----
        private void OpeningTag()
        {
            char ch = _text[_pos];
            if (IsSpace(ch))
            {
                Emit(1, null);
                return;
            }

            if (ch == '/')
            {
                Match m = SelfClosingRegex().Match(_text, _pos);
                if (!m.Success || m.Index != _pos)
                {
                    EmitError(1, SyntaxIssue.MisplacedSlash);
                    return;
                }

                _parse = XhtmlParseState.Normal;
                _tagBeingDefined = null;
                Emit(m.Length, SyntaxFormat.XhtmlTagName);
                return;
            }

            if (ch == '>')
            {
                FinishOpeningTag();
                Emit(1, SyntaxFormat.XhtmlTagName);
                return;
            }

            Match attr = AttributeNameRegex().Match(_text, _pos);
            if (!attr.Success || attr.Index != _pos)
            {
                EmitError(1, SyntaxIssue.UnknownCharacter);
                return;
            }

            _parse = XhtmlParseState.AttributeName;
            string attributeName = _attributeName = attr.Value;
            int colon = attributeName.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && colon < attributeName.Length - 1)
            {
                Emit(colon + 1, SyntaxFormat.XhtmlNamespacePrefix);
                Emit(attributeName.Length - colon - 1, SyntaxFormat.XhtmlAttributeName);
            }
            else
            {
                Emit(attributeName.Length, SyntaxFormat.XhtmlAttributeName);
            }
        }

        private void FinishOpeningTag()
        {
            _parse = XhtmlParseState.Normal;
            if (_tagBeingDefined is not { } name)
            {
                return;
            }

            _tagBeingDefined = null;
            _tags = new OpenTag(name, _tags);
            if (!_owner._isXml && CdataTags.Contains(name))
            {
                _parse = name == "style" ? XhtmlParseState.Css : XhtmlParseState.CdataContent;
                _cssState = _owner._css.InitialState;
            }
        }

        // Pops everything off the stack down to the matching open tag; if there is no match — ignores it.
        private void CloseTag(string name)
        {
            for (OpenTag? t = _tags; t is not null; t = t.Parent)
            {
                if (t.Name == name)
                {
                    _tags = t.Parent;
                    return;
                }
            }
        }

        // ---- ATTRIBUTE_NAME ----
        private void AfterAttributeName()
        {
            char ch = _text[_pos];
            if (IsSpace(ch))
            {
                Emit(1, null);
                return;
            }

            if (ch == '=')
            {
                _parse = XhtmlParseState.AttributeValue;
                Emit(1, SyntaxFormat.XhtmlAttributeName);
                return;
            }

            // An attribute without a value (e.g. <input disabled>).
            _parse = XhtmlParseState.InOpeningTag;
            _attributeName = null;
        }

        // ---- ATTRIBUTE_VALUE ----
        private void AfterEquals()
        {
            char ch = _text[_pos];
            if (IsSpace(ch))
            {
                Emit(1, null);
                return;
            }

            if (ch is '"' or '\'')
            {
                _parse = ch == '\'' ? XhtmlParseState.SingleQuotedValue : XhtmlParseState.DoubleQuotedValue;
                Emit(1, SyntaxFormat.XhtmlAttributeValue);
                return;
            }

            _parse = XhtmlParseState.InOpeningTag;
            _attributeName = null;
            Match m = UnquotedValueRegex().Match(_text, _pos);
            if (!m.Success || m.Index != _pos)
            {
                // No value: the character before the next one is flagged, so that
                // the ">" still closes the tag.
                _spans?.Add(new SyntaxSpan(Math.Max(_pos - 1, 0), 1, SyntaxFormat.SyntaxError, SyntaxIssue.MissingAttributeValue));
                return;
            }

            Emit(m.Length, SyntaxFormat.XhtmlAttributeValue);
        }

        // ---- SQ_VAL / DQ_VAL ----
        private void QuotedValue()
        {
            char quote = _parse == XhtmlParseState.DoubleQuotedValue ? '"' : '\'';
            int close = _text.IndexOf(quote, _pos);
            if (close < 0)
            {
                Emit(_text.Length - _pos, SyntaxFormat.XhtmlAttributeValue);
                return;
            }

            _parse = XhtmlParseState.InOpeningTag;
            string value = _text[_pos..close];
            bool isLink = _attributeName is { } attr && LinkAttributes.Contains(attr);
            _attributeName = null;

            string trimmed = value.Trim();
            if (isLink && trimmed.Length > 0 && !trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                bool exists = _owner._linkExists?.Invoke(trimmed) ?? true;
                if (exists)
                {
                    Emit(value.Length, SyntaxFormat.Link, SyntaxIssue.Link);
                }
                else
                {
                    Emit(value.Length, SyntaxFormat.BadLink, SyntaxIssue.BrokenLink);
                }

                Emit(1, SyntaxFormat.XhtmlAttributeValue);
                return;
            }

            Emit(value.Length + 1, SyntaxFormat.XhtmlAttributeValue);
        }

        // ---- IN_CLOSING_TAG ----
        private void ClosingTag()
        {
            char ch = _text[_pos];
            if (IsSpace(ch))
            {
                Emit(1, null);
                return;
            }

            int close = _text.IndexOf('>', _pos);
            if (close < 0)
            {
                EmitError(_text.Length - _pos, SyntaxIssue.BadClosingTag);
                return;
            }

            _parse = XhtmlParseState.Normal;
            EmitError(close - _pos, SyntaxIssue.BadClosingTag);
            Emit(1, SyntaxFormat.XhtmlTagName);
        }

        // ---- IN_COMMENT / IN_PI / IN_DOCTYPE / CDATA section ----
        private void InComment()
        {
            string end = _parse switch
            {
                XhtmlParseState.InComment => "-->",
                XhtmlParseState.InProcessingInstruction => "?>",
                XhtmlParseState.InCdataSection => "]]>",
                _ => ">",
            };
            SyntaxFormat format = _parse == XhtmlParseState.InComment ? SyntaxFormat.XhtmlComment : SyntaxFormat.XhtmlDoctype;
            int found = _text.IndexOf(end, _pos, StringComparison.Ordinal);
            if (found < 0)
            {
                Emit(_text.Length - _pos, format);
                return;
            }

            _parse = XhtmlParseState.Normal;
            Emit(found - _pos + end.Length, format);
        }

        // ---- CDATA (title, script…) ----
        private void CdataContent()
        {
            string name = _tags?.Name ?? string.Empty;
            SyntaxFormat? format = name == "title" ? SyntaxFormat.XhtmlBoldText : null;
            int close = FindClosing(name);
            if (close < 0)
            {
                Emit(_text.Length - _pos, format);
                return;
            }

            Emit(close - _pos, format);
            EnterClosingTag(name);
        }

        // ---- CSS (<style>) ----
        private void Css()
        {
            int close = FindClosing("style");
            int end = close < 0 ? _text.Length : close;
            string css = _text[_pos..end];
            List<SyntaxSpan> cssSpans = _owner._cssSpans;
            cssSpans.Clear();
            _cssState = _owner._css.HighlightLine(css, _cssState, _spans is null ? null : cssSpans);
            if (_spans is not null)
            {
                foreach (SyntaxSpan span in cssSpans)
                {
                    _spans.Add(span with { Start = span.Start + _pos });
                }
            }

            _pos = end;
            if (close >= 0)
            {
                _cssState = _owner._css.InitialState;
                EnterClosingTag("style");
            }
        }

        private int FindClosing(string name)
        {
            int from = _pos;
            while (true)
            {
                int lt = _text.IndexOf("</", from, StringComparison.Ordinal);
                if (lt < 0)
                {
                    return -1;
                }

                if (string.Compare(_text, lt + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                    name.Length > 0)
                {
                    return lt;
                }

                from = lt + 2;
            }
        }

        private void EnterClosingTag(string name)
        {
            Emit(2, SyntaxFormat.XhtmlTagName);
            Emit(name.Length, SyntaxFormat.XhtmlTagName);
            CloseTag(name);
            _parse = XhtmlParseState.InClosingTag;
        }

        private static bool IsSpace(char ch) => ch is ' ' or '\t' or '\r' or '\n' or '\f';
    }

    [GeneratedRegex(@"\G/?[a-zA-Z0-9:-]+")]
    private static partial Regex TagNameRegex();

    [GeneratedRegex(@"\G&#?[a-zA-Z0-9]{1,8};")]
    private static partial Regex EntityRegex();

    [GeneratedRegex(@"\G[^ \t\r\n\f""'/><=]+")]
    private static partial Regex AttributeNameRegex();

    [GeneratedRegex(@"\G/\s*>")]
    private static partial Regex SelfClosingRegex();

    [GeneratedRegex(@"\G[^ \t\r\n\f'""=<>`]+")]
    private static partial Regex UnquotedValueRegex();
}
