using System;
using System.Collections.Generic;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core;

/// <summary>The kind of tag recognized by <see cref="TagLister"/>.</summary>
public enum TagKind
{
    /// <summary>The initial / unrecognized state.</summary>
    Unknown,

    /// <summary>The <c>&lt;?xml ... ?&gt;</c> declaration.</summary>
    XmlHeader,

    /// <summary>A processing instruction <c>&lt;? ... ?&gt;</c>.</summary>
    ProcessingInstruction,

    /// <summary>A comment <c>&lt;!-- ... --&gt;</c>.</summary>
    Comment,

    /// <summary>The <c>&lt;!DOCTYPE ...&gt;</c> declaration.</summary>
    Doctype,

    /// <summary>A section <c>&lt;![CDATA[ ... ]]&gt;</c>.</summary>
    CData,

    /// <summary>An opening tag, e.g. <c>&lt;p&gt;</c>.</summary>
    Begin,

    /// <summary>An empty tag, e.g. <c>&lt;br /&gt;</c>.</summary>
    SelfClosing,

    /// <summary>A closing tag, e.g. <c>&lt;/p&gt;</c>.</summary>
    End,
}

/// <summary>
/// A lightweight, lenient tag scanner over XHTML/XML source text.
/// It is not a validating XML parser: it builds a flat list of tags with their positions and
/// lengths in the source and links closing tags to opening ones.
/// </summary>
/// <remarks>
/// <para>Used for (1) queries such as "is the position inside a tag / in the content / in &lt;body&gt;" (Find&amp;Replace
/// "in tags / outside tags"), (2) Code View &#8596; Preview synchronization, (3) locating
/// nesting errors as a complement to <see cref="BookManipulation.WellFormedChecker"/>.</para>
/// <para>The scanner deliberately keeps a few quirks — e.g. matching a closing tag
/// only checks that the open tag name <em>starts with</em> the closing tag name,
/// and on a mismatch the tag stack is not unwound (later positions may be inexact).</para>
/// </remarks>
public sealed class TagLister
{
    private const string WhitespaceChars = " \t\n\r";

    private string _source = string.Empty;
    private int _pos;
    private int _next;
    private int _child = -1;

    private readonly List<string> _tagPath = new();
    private readonly List<int> _tagPos = new();
    private readonly List<int> _tagLen = new();
    private readonly List<int> _tagChild = new();
    private readonly List<TagInfo> _tags = new();

    private int _bodyStartPos = -1;
    private int _bodyEndPos = -1;
    private int _bodyOpenTag = -1;
    private int _bodyCloseTag = -1;

    /// <summary>Creates the scanner and immediately builds the tag list for the given source.</summary>
    public TagLister(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Reload(source);
    }

    /// <summary>Loads a new source and rebuilds the tag list.</summary>
    public void Reload(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _pos = 0;
        _next = 0;
        _child = -1;
        _tagPath.Clear();
        _tagPath.Add("root");
        _tagPos.Clear();
        _tagPos.Add(-1);
        _tagLen.Clear();
        _tagLen.Add(0);
        _tagChild.Clear();
        _tagChild.Add(-1);
        BuildTagList();
    }

    /// <summary>The source passed to the scanner.</summary>
    public string Source => _source;

    /// <summary>
    /// The number of list entries — including the closing sentinel entry
    /// (<c>Pos == -1</c>, <c>Len == -1</c>).
    /// </summary>
    public int Count => _tags.Count;

    /// <summary>All tags (the last entry is the end-of-list sentinel).</summary>
    public IReadOnlyList<TagInfo> Tags => _tags;

    /// <summary>
    /// The entry at index <paramref name="i"/>; an out-of-range index returns the last entry
    /// (the sentinel).
    /// </summary>
    public TagInfo At(int i)
    {
        if (i < 0 || i >= _tags.Count)
        {
            i = _tags.Count - 1;
        }

        return _tags[i];
    }

    /// <summary>The index of the opening <c>&lt;body&gt;</c> tag or <c>-1</c>.</summary>
    public int FindBodyOpenTag() => _bodyOpenTag;

    /// <summary>The index of the closing <c>&lt;/body&gt;</c> tag or <c>-1</c>.</summary>
    public int FindBodyCloseTag() => _bodyCloseTag;

    /// <summary>Whether position <paramref name="pos"/> lies inside the contents of <c>&lt;body&gt;</c>.</summary>
    public bool IsPositionInBody(int pos) => pos >= _bodyStartPos && pos <= _bodyEndPos;

    /// <summary>Whether position <paramref name="pos"/> lies inside any tag.</summary>
    public bool IsPositionInTag(int pos)
    {
        TagInfo ti = At(FindFirstTagOnOrAfter(pos));
        return pos >= ti.Pos && pos < ti.Pos + ti.Len;
    }

    /// <summary>Whether position <paramref name="pos"/> lies inside an opening / empty tag.</summary>
    public bool IsPositionInOpenTag(int pos)
    {
        TagInfo ti = At(FindFirstTagOnOrAfter(pos));
        return pos >= ti.Pos && pos < ti.Pos + ti.Len
            && ti.Kind is TagKind.Begin or TagKind.SelfClosing;
    }

    /// <summary>Whether position <paramref name="pos"/> lies inside a closing tag.</summary>
    public bool IsPositionInCloseTag(int pos)
    {
        TagInfo ti = At(FindFirstTagOnOrAfter(pos));
        return pos >= ti.Pos && pos < ti.Pos + ti.Len && ti.Kind == TagKind.End;
    }

    /// <summary>
    /// The index of the tag that starts at position <paramref name="pos"/> or before it.
    /// May return <c>-1</c> if there is none.
    /// </summary>
    public int FindLastTagOnOrBefore(int pos)
    {
        int i = 0;
        TagInfo ti = _tags[i];
        while (ti.Pos <= pos && ti.Len != -1)
        {
            i++;
            ti = _tags[i];
        }

        return i - 1;
    }

    /// <summary>
    /// The index of the first tag that ends after position <paramref name="pos"/>. The list is
    /// terminated by a sentinel, so the result always points at a valid entry (possibly the sentinel).
    /// </summary>
    public int FindFirstTagOnOrAfter(int pos)
    {
        int i = 0;
        TagInfo ti = _tags[i];
        while (ti.Pos + ti.Len <= pos && ti.Len != -1)
        {
            i++;
            ti = _tags[i];
        }

        return i;
    }

    /// <summary>For a closing-tag entry — the index of its opening tag.</summary>
    public int FindOpenTagForClose(int i)
    {
        if (i < 0 || i >= _tags.Count)
        {
            return -1;
        }

        TagInfo ti = _tags[i];
        if (ti.Kind != TagKind.End)
        {
            return -1;
        }

        for (int j = i - 1; j >= 0; j--)
        {
            if (_tags[j].Pos == ti.OpenPos)
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>For an opening-tag entry — the index of its closing tag.</summary>
    public int FindCloseTagForOpen(int i)
    {
        if (i < 0 || i >= _tags.Count)
        {
            return -1;
        }

        TagInfo ti = _tags[i];
        if (ti.Kind != TagKind.Begin)
        {
            return -1;
        }

        for (int j = i + 1; j < _tags.Count; j++)
        {
            if (_tags[j].OpenPos == ti.Pos)
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of the last opening / empty tag that encloses position
    /// <paramref name="pos"/> (used for synchronization with Preview). The position is first clamped
    /// to the inside of <c>&lt;body&gt;</c>.
    /// </summary>
    public int FindLastOpenOrSingleTagThatContainsYou(int pos)
    {
        int bpos = pos;
        if (bpos > _bodyEndPos)
        {
            bpos = _bodyEndPos;
        }

        if (bpos < _bodyStartPos)
        {
            bpos = _bodyStartPos;
        }

        int k = FindLastTagOnOrBefore(bpos);
        TagInfo ti = At(k);

        if (ti.Kind == TagKind.SelfClosing && bpos >= ti.Pos && bpos < ti.Pos + ti.Len)
        {
            return k;
        }

        if (ti.Kind == TagKind.Begin)
        {
            int ci = FindCloseTagForOpen(k);
            if (ci != -1)
            {
                TagInfo cls = _tags[ci];
                if (bpos >= ti.Pos && bpos < cls.Pos + cls.Len)
                {
                    return k;
                }
            }
        }

        int i = k;
        bool found = false;
        while (i >= 0 && !found)
        {
            TagKind kind = At(i).Kind;
            if (kind is TagKind.SelfClosing or TagKind.Begin)
            {
                found = true;
            }
            else
            {
                i--;
            }
        }

        return found ? i : -1;
    }

    /// <summary>
    /// The index of the last opening tag at position <paramref name="pos"/> or before it
    /// (the position is clamped to the inside of <c>&lt;body&gt;</c>).
    /// </summary>
    public int FindLastOpenTagOnOrBefore(int pos)
    {
        int bpos = pos;
        if (bpos >= _bodyEndPos)
        {
            bpos = _bodyEndPos;
        }

        if (bpos <= _bodyStartPos)
        {
            bpos = _bodyStartPos;
        }

        int i = FindLastTagOnOrBefore(bpos);
        bool found = false;
        while (i >= 0 && !found)
        {
            if (At(i).Kind == TagKind.Begin)
            {
                found = true;
            }
            else
            {
                i--;
            }
        }

        return found ? i : -1;
    }

    /// <summary>
    /// The tag path (with child numbers) to the tag enclosing position
    /// <paramref name="pos"/>, or <c>"html -1"</c> if it cannot be determined.
    /// </summary>
    public string GeneratePathToTag(int pos)
    {
        int i = FindLastOpenOrSingleTagThatContainsYou(pos);
        return i < 0 ? "html -1" : _tags[i].TagPath;
    }

    /// <summary>
    /// The first detected nesting error (an unmatched closing tag or a tag
    /// that is never closed), or <c>null</c> if the nesting is correct. Complements
    /// <see cref="BookManipulation.WellFormedChecker"/> with a clearer message and position.
    /// </summary>
    public NestingError? FindFirstNestingError()
    {
        Stack<TagInfo> open = new();
        foreach (TagInfo ti in _tags)
        {
            if (ti.Len == -1)
            {
                break;
            }

            switch (ti.Kind)
            {
                case TagKind.Begin:
                    open.Push(ti);
                    break;

                case TagKind.End when ti.OpenPos == -1 || open.Count == 0:
                    return new NestingError(ti.Pos, ti.TagName, CoreStrings.Format("Nesting_UnmatchedClosingTag", ti.TagName));

                case TagKind.End:
                    open.Pop();
                    break;
            }
        }

        if (open.Count == 0)
        {
            return null;
        }

        TagInfo earliest = open.Peek();
        foreach (TagInfo ti in open)
        {
            if (ti.Pos < earliest.Pos)
            {
                earliest = ti;
            }
        }

        return new NestingError(earliest.Pos, earliest.TagName, CoreStrings.Format("Nesting_UnclosedTag", earliest.TagName));
    }

    /// <summary>
    /// Finds the attribute <paramref name="attributeName"/> in the tag text and returns its position
    /// (relative to the start of the tag). <see cref="AttInfo.Pos"/> <c>== -1</c> means the attribute is absent.
    /// </summary>
    public static AttInfo ParseAttribute(string tagString, string attributeName)
    {
        ArgumentNullException.ThrowIfNull(tagString);
        ArgumentNullException.ThrowIfNull(attributeName);

        AttInfo ainfo = AttInfo.None;
        if (tagString.Length < 2)
        {
            return ainfo;
        }

        char c = tagString[1];
        if (c is '?' or '!')
        {
            return ainfo;
        }

        int p = SkipAnyBlanks(tagString, 1);
        if (p < tagString.Length && tagString[p] == '/')
        {
            return ainfo;
        }

        p = StopWhenContains(tagString, ">/ \f\t\r\n", p);

        while (IndexOfFrom(tagString, '=', p) != -1)
        {
            p = SkipAnyBlanks(tagString, p);
            int s = p;
            p = StopWhenContains(tagString, "=", p);
            string aname = Slice(tagString, s, p).Trim();
            bool wanted = string.Equals(aname, attributeName, StringComparison.Ordinal);
            if (wanted)
            {
                ainfo = ainfo with { Pos = s, AName = aname };
            }

            p++;
            p = SkipAnyBlanks(tagString, p);
            if (p < tagString.Length && (tagString[p] == '\'' || tagString[p] == '"'))
            {
                char quote = tagString[p];
                p++;
                int b = p;
                p = StopWhenContains(tagString, quote.ToString(), p);
                if (wanted)
                {
                    ainfo = ainfo with { AValue = Slice(tagString, b, p), Len = p - s + 1, VPos = b, VLen = p - b };
                }

                p++;
            }
            else
            {
                int b = p;
                p = StopWhenContains(tagString, ">/ ", p);
                if (wanted)
                {
                    ainfo = ainfo with { AValue = Slice(tagString, b, p), Len = p - s, VPos = b, VLen = p - b };
                }
            }
        }

        return ainfo;
    }

    /// <summary>
    /// Composes an attribute as <c>name="value"</c> (with an apostrophe if the value contains <c>"</c>).
    /// </summary>
    public static string SerializeAttribute(string aname, string avalue)
    {
        ArgumentNullException.ThrowIfNull(aname);
        ArgumentNullException.ThrowIfNull(avalue);
        char quote = avalue.Contains('"', StringComparison.Ordinal) ? '\'' : '"';
        return $"{aname}={quote}{avalue}{quote}";
    }

    /// <summary>
    /// Returns the copied fragment of the tag with all attributes (without the tag name and without
    /// the trailing <c>&gt;</c> / <c>/&gt;</c>), or an empty string.
    /// </summary>
    public static string ExtractAllAttributes(string tagString)
    {
        ArgumentNullException.ThrowIfNull(tagString);
        if (tagString.Length < 2)
        {
            return string.Empty;
        }

        int taglen = tagString.Length;
        char c = tagString[1];
        if (c is '?' or '!')
        {
            return string.Empty;
        }

        int p = SkipAnyBlanks(tagString, 1);
        if (p < taglen && tagString[p] == '/')
        {
            return string.Empty;
        }

        p = StopWhenContains(tagString, ">/ \f\t\r\n", p);
        p = SkipAnyBlanks(tagString, p);

        if (IndexOfFrom(tagString, '=', p) == -1)
        {
            return string.Empty;
        }

        string res = Slice(tagString, p, taglen - 1).Trim();
        if (res.EndsWith('/'))
        {
            res = res[..^1].Trim();
        }

        return res;
    }

    // =====================================================================
    //  Building the tag list
    // =====================================================================

    private void BuildTagList()
    {
        _tags.Clear();
        _bodyStartPos = -1;
        _bodyEndPos = -1;
        _bodyOpenTag = -1;
        _bodyCloseTag = -1;

        int i = 0;
        TagInfo ti = GetNext();
        while (ti.Len != -1)
        {
            if (ti is { TagName: "body", Kind: TagKind.Begin })
            {
                _bodyStartPos = ti.Pos + ti.Len;
                _bodyOpenTag = i;
            }

            if (ti is { TagName: "body", Kind: TagKind.End })
            {
                _bodyEndPos = ti.Pos - 1;
                _bodyCloseTag = i;
            }

            _tags.Add(ti);
            i++;
            ti = GetNext();
        }

        _tags.Add(new TagInfo { Pos = -1, Len = -1, Child = -1, OpenPos = -1, OpenLen = -1, TagName = string.Empty, TagPath = string.Empty });
    }

    private TagInfo GetNext()
    {
        TagInfo mi = new() { Pos = -1, Len = -1, Child = -1, OpenPos = -1, OpenLen = -1, TagName = string.Empty, TagPath = string.Empty };
        string? markup = ParseMarkup();
        while (markup is not null)
        {
            if (markup.Length > 0 && markup[0] == '<' && markup[^1] == '>')
            {
                mi.Pos = _pos;
                ParseTag(markup, mi);

                switch (mi.Kind)
                {
                    case TagKind.Begin:
                        _tagPos.Add(mi.Pos);
                        _tagLen.Add(mi.Len);
                        mi.Child = ++_child;
                        _tagChild.Add(mi.Child);
                        _child = -1;
                        _tagPath.Add(mi.TagName);
                        mi.TagPath = MakePathToTag();
                        break;

                    case TagKind.SelfClosing:
                        mi.Child = ++_child;
                        _tagChild.Add(mi.Child);
                        _tagPath.Add(mi.TagName);
                        mi.TagPath = MakePathToTag();
                        _tagPath.RemoveAt(_tagPath.Count - 1);
                        _tagChild.RemoveAt(_tagChild.Count - 1);
                        break;

                    case TagKind.End:
                        string pathNode = _tagPath[^1];
                        if (pathNode.StartsWith(mi.TagName, StringComparison.Ordinal))
                        {
                            _tagPath.RemoveAt(_tagPath.Count - 1);
                            mi.OpenPos = TakeLast(_tagPos);
                            mi.OpenLen = TakeLast(_tagLen);
                            mi.Child = TakeLast(_tagChild);
                            _child = mi.Child;
                        }
                        else
                        {
                            mi.OpenPos = -1;
                            mi.OpenLen = -1;
                            mi.Child = -1;
                        }

                        mi.TagPath = MakePathToTag();
                        break;
                }

                return mi;
            }

            markup = ParseMarkup();
        }

        return mi;
    }

    private string MakePathToTag()
    {
        StringBuilder sb = new();
        for (int i = 1; i < _tagPath.Count; i++)
        {
            int childIndex = -1;
            if (i + 1 < _tagPath.Count)
            {
                childIndex = _tagChild[i + 1];
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(_tagPath[i]).Append(' ').Append(childIndex);
        }

        return sb.ToString();
    }

    private string? ParseMarkup()
    {
        int p = _next;
        _pos = p;
        if (p >= _source.Length)
        {
            return null;
        }

        _next = MarkupEnd(_source, p);
        return Slice(_source, _pos, _next);
    }

    /// <summary>
    /// The opening and empty tags (<see cref="TagKind.Begin"/>, <see cref="TagKind.SelfClosing"/>) of
    /// <paramref name="source"/> as (position, length) — the same tags as in <see cref="Tags"/>, found with the same
    /// rules, but without building the tag list, the tag paths or any strings. For callers that only look into the
    /// attributes of the tags.
    /// </summary>
    public static IEnumerable<(int Pos, int Len)> EnumerateOpeningTags(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int p = 0;
        while (p < source.Length)
        {
            int next = MarkupEnd(source, p);
            if (IsOpeningTag(source.AsSpan(p, next - p)))
            {
                yield return (p, next - p);
            }

            p = next;
        }
    }

    // Mirrors the Begin / SelfClosing classification of GetNext + ParseTag: markup "<…>" that is not "<?…", "<!…"
    // or a closing tag "</…" (blanks allowed after "<").
    private static bool IsOpeningTag(ReadOnlySpan<char> markup)
    {
        if (markup.Length < 2 || markup[0] != '<' || markup[^1] != '>' || markup[1] is '?' or '!')
        {
            return false;
        }

        int p = 1;
        while (p < markup.Length && WhitespaceChars.Contains(markup[p], StringComparison.Ordinal))
        {
            p++;
        }

        return p >= markup.Length || markup[p] != '/';
    }

    // The end (exclusive) of the markup chunk — text, comment, CDATA section or tag — starting at p.
    private static int MarkupEnd(string source, int p)
    {
        if (source[p] != '<')
        {
            return FindTarget(source, "<", p + 1);
        }

        ReadOnlySpan<char> rest = source.AsSpan(p);
        if (rest.StartsWith("<!--", StringComparison.Ordinal))
        {
            return FindTarget(source, "-->", p + 4, after: true);
        }

        if (rest.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase))
        {
            return FindTarget(source, "]]>", p + 9, after: true);
        }

        int next = FindTarget(source, ">", p + 1, after: true);
        int nextTagStart = FindTarget(source, "<", p + 1);
        return Math.Min(next, nextTagStart);
    }

    private static void ParseTag(string tagString, TagInfo mi)
    {
        mi.Len = tagString.Length;
        char c = tagString[1];

        if (c == '?')
        {
            if (tagString.StartsWith("<?xml", StringComparison.Ordinal))
            {
                mi.TagName = "?xml";
                mi.Kind = TagKind.XmlHeader;
            }
            else
            {
                mi.TagName = "?";
                mi.Kind = TagKind.ProcessingInstruction;
            }

            return;
        }

        if (c == '!')
        {
            if (tagString.StartsWith("<!--", StringComparison.Ordinal))
            {
                mi.TagName = "!--";
                mi.Kind = TagKind.Comment;
            }
            else if (tagString.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                mi.TagName = "!DOCTYPE";
                mi.Kind = TagKind.Doctype;
            }
            else if (tagString.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase))
            {
                mi.TagName = "![CDATA[";
                mi.Kind = TagKind.CData;
            }

            return;
        }

        int p = SkipAnyBlanks(tagString, 1);
        if (p < tagString.Length && tagString[p] == '/')
        {
            mi.Kind = TagKind.End;
            p++;
            p = SkipAnyBlanks(tagString, p);
        }

        int b = p;
        p = StopWhenContains(tagString, ">/ \f\t\r\n", p);
        mi.TagName = Slice(tagString, b, p);

        if (mi.Kind == TagKind.Unknown)
        {
            mi.Kind = tagString.EndsWith("/>", StringComparison.Ordinal) || tagString.EndsWith("/ >", StringComparison.Ordinal)
                ? TagKind.SelfClosing
                : TagKind.Begin;
        }
    }

    private static int FindTarget(string source, string target, int start, bool after = false)
    {
        int found = source.IndexOf(target, Math.Min(Math.Max(start, 0), source.Length), StringComparison.Ordinal);
        if (found == -1)
        {
            return source.Length;
        }

        found += target.Length - 1;
        if (after)
        {
            found++;
        }

        return found;
    }

    private static int TakeLast(List<int> list)
    {
        int value = list[^1];
        list.RemoveAt(list.Count - 1);
        return value;
    }

    private static int IndexOfFrom(string s, char value, int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        return from >= s.Length ? -1 : s.IndexOf(value, from);
    }

    private static string Slice(string s, int start, int end)
    {
        if (start < 0)
        {
            start = 0;
        }

        if (start > s.Length)
        {
            start = s.Length;
        }

        if (end > s.Length)
        {
            end = s.Length;
        }

        return end <= start ? string.Empty : s[start..end];
    }

    private static int SkipAnyBlanks(string s, int p)
    {
        while (p < s.Length && WhitespaceChars.Contains(s[p], StringComparison.Ordinal))
        {
            p++;
        }

        return p;
    }

    private static int StopWhenContains(string s, string stopChars, int p)
    {
        while (p < s.Length && !stopChars.Contains(s[p], StringComparison.Ordinal))
        {
            p++;
        }

        return p;
    }

    /// <summary>
    /// A single entry of the tag list. All positions are counted in characters from the start of the source.
    /// </summary>
    public sealed class TagInfo
    {
        /// <summary>The position of the tag start in the source (<c>-1</c> for the sentinel).</summary>
        public int Pos { get; internal set; } = -1;

        /// <summary>The length of the tag in the source (<c>-1</c> for the sentinel).</summary>
        public int Len { get; internal set; } = -1;

        /// <summary>The number of this tag among its parent's children (0-based).</summary>
        public int Child { get; internal set; } = -1;

        /// <summary>The path of tag names with child numbers, e.g. <c>"html 0,body 0,p 1"</c>.</summary>
        public string TagPath { get; internal set; } = string.Empty;

        /// <summary>The tag name (or <c>?xml</c>, <c>?</c>, <c>!--</c>, <c>!DOCTYPE</c>, <c>![CDATA[</c>).</summary>
        public string TagName { get; internal set; } = string.Empty;

        /// <summary>The tag kind.</summary>
        public TagKind Kind { get; internal set; } = TagKind.Unknown;

        /// <summary>For a closing tag — the position of the matching opening tag (<c>-1</c> on a mismatch).</summary>
        public int OpenPos { get; internal set; } = -1;

        /// <summary>For a closing tag — the length of the matching opening tag.</summary>
        public int OpenLen { get; internal set; } = -1;
    }

    /// <summary>The position of a single attribute in the tag text (everything relative to the start of the tag).</summary>
    public readonly record struct AttInfo(int Pos, int Len, int VPos, int VLen, string AName, string AValue)
    {
        /// <summary>"No attribute" — all positions <c>-1</c>, name/value empty.</summary>
        public static AttInfo None { get; } = new(-1, -1, -1, -1, string.Empty, string.Empty);
    }
}

/// <summary>A tag nesting error detected by <see cref="TagLister.FindFirstNestingError"/>.</summary>
public readonly record struct NestingError(int Pos, string TagName, string Message);
