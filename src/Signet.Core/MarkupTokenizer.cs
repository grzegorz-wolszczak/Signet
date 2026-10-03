using System;
using System.Collections.Generic;

namespace Signet.Core;

/// <summary>The kind of token returned by <see cref="MarkupTokenizer"/>.</summary>
internal enum MarkupTokenType
{
    /// <summary>The initial state / an unrecognized token.</summary>
    None,

    /// <summary>A text node (content between tags).</summary>
    Text,

    /// <summary>An opening tag, e.g. <c>&lt;metadata&gt;</c>.</summary>
    Begin,

    /// <summary>A closing tag, e.g. <c>&lt;/metadata&gt;</c>.</summary>
    End,

    /// <summary>An empty tag, e.g. <c>&lt;item ... /&gt;</c>.</summary>
    Single,

    /// <summary>The <c>&lt;?xml ... ?&gt;</c> declaration.</summary>
    XmlHeader,

    /// <summary>A processing instruction <c>&lt;? ... ?&gt;</c>.</summary>
    ProcessingInstruction,

    /// <summary>A comment <c>&lt;!-- ... --&gt;</c>.</summary>
    Comment,

    /// <summary>A <c>&lt;![CDATA[ ... ]]&gt;</c> section.</summary>
    CData,
}

/// <summary>A single token produced by <see cref="MarkupTokenizer"/>.</summary>
internal sealed class MarkupToken
{
    /// <summary>The token's position in the source (the index of its first character); -1 when absent.</summary>
    public int Pos { get; set; } = -1;

    /// <summary>The content when the token is a text node; otherwise empty.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The dot-separated tag path, e.g. <c>root.package.manifest</c>.</summary>
    public string TagPath { get; set; } = string.Empty;

    /// <summary>
    /// The language inherited from the nearest ancestor with a <c>lang</c>/<c>xml:lang</c> attribute
    /// (or the default language passed to the constructor when no ancestor sets one).
    /// Used by word extraction for spellchecking.
    /// </summary>
    public string Lang { get; set; } = string.Empty;

    /// <summary>The tag name (without brackets), e.g. <c>item</c>.</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>The token kind.</summary>
    public MarkupTokenType TagType { get; set; } = MarkupTokenType.None;

    /// <summary>The tag's attributes in order of occurrence.</summary>
    public TagAttributes Attributes { get; } = new();
}

/// <summary>
/// A tolerant, linear markup scanner (not a validating XML parser). Used to parse OPF files, which in
/// practice are often syntactically invalid (unclosed tags, mixed namespace prefixes). It also tracks
/// the language path (<see cref="MarkupToken.Lang"/>) through the <c>lang</c>/<c>xml:lang</c> attributes, so
/// the same tokenization logic serves both OPF parsing and spellcheck word extraction.
/// </summary>
/// <remarks>
/// The scanner preserves attribute order and the original values; comments, CDATA and PIs are
/// recognized, but <c>OPFParser</c> does not persist them (the OPF is
/// regenerated from the structure). It also detects the need to remap the namespace when the
/// <c>package</c> element has a prefix (e.g. <c>&lt;opf:package&gt;</c>). <c>&lt;!DOCTYPE&gt;</c> (present
/// in XHTML documents) is recognized as an opaque token with no effect on the tag/language path.
/// </remarks>
internal sealed class MarkupTokenizer
{
    private const string WhitespaceChars = " \t\n\r";
    private const string OpfNamespace = "http://www.idpf.org/2007/opf";

    private readonly string _source;
    private readonly List<string> _tagPath = new() { "root" };
    private readonly List<string> _langPath;
    private int _pos;
    private int _next;
    private bool _nsRemap;
    private string _oldPrefix = string.Empty;

    /// <summary>Creates a scanner for the given source.</summary>
    /// <param name="source">The content to parse.</param>
    /// <param name="defaultLang">
    /// The language used until an ancestor sets <c>lang</c>/<c>xml:lang</c>.
    /// </param>
    public MarkupTokenizer(string source, string defaultLang = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(defaultLang);
        _source = source;
        _langPath = new List<string> { defaultLang };
    }

    /// <summary>The default OPF namespace (used when remapping the prefix).</summary>
    public static string DefaultOpfNamespace => OpfNamespace;

    /// <summary>Whether a <c>package</c> element with a namespace prefix was detected in the source.</summary>
    public bool NsRemapNeeded => _nsRemap;

    /// <summary>The namespace prefix used on the <c>package</c> element (when applicable).</summary>
    public string OldPrefix => _oldPrefix;

    /// <summary>Returns the next token or <c>null</c> once the end of the source is reached.</summary>
    public MarkupToken? ParseNext()
    {
        MarkupToken mi = new();
        string? markup = ParseMarkup();
        if (markup is null)
        {
            return null;
        }

        if (markup.Length > 0 && markup[0] == '<' && markup[^1] == '>')
        {
            ParseTag(markup, mi);

            if (mi.TagName.EndsWith(":package", StringComparison.Ordinal) && mi.TagType == MarkupTokenType.Begin)
            {
                _nsRemap = true;
                _oldPrefix = mi.TagName.Split(':')[0];
            }

            if (_nsRemap && mi.TagName.StartsWith(_oldPrefix + ":", StringComparison.Ordinal))
            {
                mi.TagName = mi.TagName[(_oldPrefix.Length + 1)..];
            }

            if (mi.TagType == MarkupTokenType.Begin)
            {
                _tagPath.Add(mi.TagName);
                string lang = mi.Attributes.Value("lang");
                if (lang.Length == 0)
                {
                    lang = mi.Attributes.Value("xml:lang");
                }

                if (lang.Length == 0)
                {
                    lang = _langPath[^1];
                }

                _langPath.Add(lang);
            }
            else if (mi.TagType == MarkupTokenType.End)
            {
                if (_tagPath.Count > 0)
                {
                    _tagPath.RemoveAt(_tagPath.Count - 1);
                }

                if (_langPath.Count > 1)
                {
                    _langPath.RemoveAt(_langPath.Count - 1);
                }
            }
        }
        else
        {
            mi.Text = markup;
        }

        mi.Pos = _pos;
        mi.TagPath = string.Join('.', _tagPath);
        mi.Lang = _langPath[^1];
        return mi;
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

    private static bool HasCharFrom(string s, char value, int from)
    {
        for (int i = Math.Max(from, 0); i < s.Length; i++)
        {
            if (s[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static void ParseTag(string tagstring, MarkupToken mi)
    {
        int taglen = tagstring.Length;
        char c = tagstring[1];

        if (c == '?')
        {
            if (tagstring.StartsWith("<?xml", StringComparison.Ordinal))
            {
                mi.TagName = "?xml";
                mi.TagType = MarkupTokenType.XmlHeader;
                mi.Attributes.Set("special", Slice(tagstring, 5, taglen - 1));
            }
            else
            {
                mi.TagName = "?";
                mi.TagType = MarkupTokenType.ProcessingInstruction;
                mi.Attributes.Set("special", Slice(tagstring, 1, taglen - 1));
            }

            return;
        }

        if (c == '!')
        {
            if (tagstring.StartsWith("<!--", StringComparison.Ordinal))
            {
                mi.TagName = "!--";
                mi.TagType = MarkupTokenType.Comment;
                mi.Attributes.Set("special", Slice(tagstring, 4, taglen - 3));
            }
            else if (tagstring.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase))
            {
                mi.TagName = "![CDATA[";
                mi.TagType = MarkupTokenType.CData;
                mi.Attributes.Set("special", Slice(tagstring, 9, taglen - 3));
            }

            return;
        }

        int p = SkipAnyBlanks(tagstring, 1);
        if (p < taglen && tagstring[p] == '/')
        {
            mi.TagType = MarkupTokenType.End;
            p++;
            p = SkipAnyBlanks(tagstring, p);
        }

        int b = p;
        p = StopWhenContains(tagstring, ">/ \f\t\r\n", p);
        mi.TagName = Slice(tagstring, b, p);

        if (mi.TagType != MarkupTokenType.None)
        {
            return;
        }

        while (HasCharFrom(tagstring, '=', p))
        {
            p = SkipAnyBlanks(tagstring, p);
            b = p;
            p = StopWhenContains(tagstring, "=", p);
            string aname = Slice(tagstring, b, p).Trim();
            string avalue;
            p++;
            p = SkipAnyBlanks(tagstring, p);
            if (p < taglen && (tagstring[p] == '\'' || tagstring[p] == '"'))
            {
                char quote = tagstring[p];
                p++;
                b = p;
                p = StopWhenContains(tagstring, quote.ToString(), p);
                avalue = Slice(tagstring, b, p);
                p++;
            }
            else
            {
                b = p;
                p = StopWhenContains(tagstring, ">/ ", p);
                avalue = Slice(tagstring, b, p);
            }

            if (aname.Length > 0)
            {
                mi.Attributes.Set(aname, avalue);
            }
        }

        mi.TagType = HasCharFrom(tagstring, '/', p) ? MarkupTokenType.Single : MarkupTokenType.Begin;
    }

    private string? ParseMarkup()
    {
        int p = _next;
        _pos = p;
        if (p >= _source.Length)
        {
            return null;
        }

        if (_source[p] != '<')
        {
            _next = FindTarget("<", p + 1);
            return Slice(_source, _pos, _next);
        }

        string tstart = Slice(_source, p, p + 9);
        if (tstart.StartsWith("<!--", StringComparison.Ordinal))
        {
            _next = FindTarget("-->", p + 4, after: true);
            return Slice(_source, _pos, _next);
        }

        if (tstart.StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            _next = FindTarget("]]>", p + 9, after: true);
            return Slice(_source, _pos, _next);
        }

        _next = FindTarget(">", p + 1, after: true);
        int nextTagStart = FindTarget("<", p + 1);
        if (nextTagStart < _next)
        {
            _next = nextTagStart;
        }

        return Slice(_source, _pos, _next);
    }

    private int FindTarget(string target, int start, bool after = false)
    {
        int found = _source.IndexOf(target, Math.Min(Math.Max(start, 0), _source.Length), StringComparison.Ordinal);
        if (found == -1)
        {
            return _source.Length;
        }

        found += target.Length - 1;
        if (after)
        {
            found++;
        }

        return found;
    }
}
