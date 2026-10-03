using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.Core.Toc;

/// <summary>
/// An in-memory model of an NCX document (<c>toc.ncx</c>): <c>head</c> (<c>dtb:*</c> meta), <c>docTitle</c>,
/// <c>docAuthor</c>, the <c>navMap</c> tree (<see cref="NcxNavPoint"/>), <c>pageList</c> and <c>navList</c>.
/// The counterpart of what <see cref="OpfDocument"/> is for the OPF.
/// </summary>
/// <remarks>
/// Parsing is lenient (<see cref="MarkupTokenizer"/> — the same scanner as for the OPF): a file
/// with invalid syntax does not throw, a "best effort" model is produced (well-formedness validation is
/// a separate concern). <see cref="ToXml"/> recreates the XML in a fixed format (not byte for byte relative to the
/// input); the round trip <c>Parse → ToXml → Parse</c> is stable. On write <c>playOrder</c>
/// is recalculated from the tree order (preorder), and an empty <c>id</c> in a <c>navPoint</c> is filled in
/// as <c>navPoint-{playOrder}</c>. The values <c>dtb:depth</c> / <c>dtb:totalPageCount</c> /
/// <c>dtb:maxPageNumber</c> in <c>head</c> are <b>not</b> touched by <see cref="ToXml"/> —
/// <see cref="SyncHeadCounts"/> refreshes them (used by the TOC generators).
/// Comments, CDATA and processing instructions are not persisted (as in <see cref="OpfDocument"/>).
/// </remarks>
public sealed class NcxDocument
{
    private const string NcxNamespace = "http://www.daisy.org/z3986/2005/ncx/";

    private const string DoctypeLine =
        "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">";

    /// <summary>The value of <c>ncx/@version</c> (practically always <c>2005-1</c>).</summary>
    public string Version { get; set; } = "2005-1";

    /// <summary>
    /// Whether <see cref="ToXml"/> should write the <c>&lt;!DOCTYPE ncx …&gt;</c> declaration. Set by
    /// <see cref="Parse"/> based on the presence of a DOCTYPE in the source and by <see cref="CreateEmpty"/>
    /// (EPUB 2 =&gt; <c>true</c>, EPUB 3 =&gt; <c>false</c> — under XHTML5 a DOCTYPE is not allowed for NCX).
    /// </summary>
    public bool IncludeDoctype { get; set; }

    /// <summary>The other attributes of the <c>ncx</c> element (besides <c>xmlns</c> and <c>version</c>), in order of occurrence.</summary>
    public TagAttributes NcxExtraAttributes { get; private set; } = new();

    /// <summary>The <c>&lt;meta&gt;</c> entries from <c>&lt;head&gt;</c> in order of occurrence.</summary>
    public List<NcxMeta> Head { get; } = new();

    /// <summary>The text from <c>&lt;docTitle&gt;&lt;text&gt;</c> (decoded).</summary>
    public string DocTitle { get; set; } = string.Empty;

    /// <summary>The texts from successive <c>&lt;docAuthor&gt;&lt;text&gt;</c> elements.</summary>
    public List<string> DocAuthors { get; } = new();

    /// <summary>The top-level entries of the <c>&lt;navMap&gt;</c> tree.</summary>
    public List<NcxNavPoint> NavMap { get; } = new();

    /// <summary>The <c>&lt;pageList&gt;</c> entries (empty if the section is absent).</summary>
    public List<NcxPageTarget> PageList { get; } = new();

    /// <summary>The <c>&lt;navList&gt;</c> entries (empty if the section is absent).</summary>
    public List<NcxNavList> NavLists { get; } = new();

    /// <summary>
    /// The value of <c>&lt;meta name="dtb:uid"&gt;</c>. The getter returns <c>""</c> if the meta is absent; the setter
    /// updates the existing entry or prepends it to <see cref="Head"/>.
    /// </summary>
    public string DtbUid
    {
        get => GetHeadMeta("dtb:uid");
        set => SetHeadMeta("dtb:uid", value);
    }

    /// <summary>The depth of the <c>navMap</c> tree (0 for empty, 1 for a flat list).</summary>
    public int Depth => MaxDepth(NavMap, 1);

    /// <summary>Parses NCX text into the model (leniently — does not throw on invalid XML).</summary>
    public static NcxDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        NcxDocument document = new();
        document.ParseInternal(source);
        return document;
    }

    /// <summary>
    /// Creates a minimal NCX: <c>head</c> with <c>dtb:uid</c> = <paramref name="uid"/> and zero
    /// <c>dtb:depth</c> / <c>dtb:totalPageCount</c> / <c>dtb:maxPageNumber</c>, <c>docTitle</c> =
    /// <paramref name="title"/> and an empty <c>navMap</c>.
    /// </summary>
    /// <param name="title">The publication title (goes to <c>docTitle</c>).</param>
    /// <param name="uid">The publication identifier (goes to <c>dtb:uid</c>).</param>
    /// <param name="epub2">Whether to write the DOCTYPE (EPUB 2). <c>true</c> by default.</param>
    public static NcxDocument CreateEmpty(string title, string uid, bool epub2 = true)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(uid);

        NcxDocument document = new() { DocTitle = title, IncludeDoctype = epub2 };
        document.Head.Add(new NcxMeta("dtb:uid", uid));
        document.Head.Add(new NcxMeta("dtb:depth", "0"));
        document.Head.Add(new NcxMeta("dtb:totalPageCount", "0"));
        document.Head.Add(new NcxMeta("dtb:maxPageNumber", "0"));
        return document;
    }

    /// <summary>
    /// Refreshes the <c>head</c> entries: <c>dtb:depth</c> = <see cref="Depth"/>, <c>dtb:totalPageCount</c> and
    /// <c>dtb:maxPageNumber</c> based on <see cref="PageList"/>. Not called automatically
    /// by <see cref="ToXml"/> (the TOC generators use it).
    /// </summary>
    public void SyncHeadCounts()
    {
        SetHeadMeta("dtb:depth", Depth.ToString(CultureInfo.InvariantCulture));

        int maxPage = 0;
        foreach (NcxPageTarget target in PageList)
        {
            if (int.TryParse(target.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > maxPage)
            {
                maxPage = value;
            }
        }

        SetHeadMeta("dtb:totalPageCount", PageList.Count.ToString(CultureInfo.InvariantCulture));
        SetHeadMeta("dtb:maxPageNumber", maxPage.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Recalculates <c>playOrder</c> in all entries: preorder over the <c>navMap</c> tree, then
    /// <c>pageList</c>, then <c>navList</c> — a shared numbering, starting from 1
    /// (as the NCX specification requires).
    /// </summary>
    public void RecomputePlayOrder()
    {
        int order = 1;

        void WalkNavPoint(NcxNavPoint navPoint)
        {
            navPoint.PlayOrder = order++;
            foreach (NcxNavPoint child in navPoint.Children)
            {
                WalkNavPoint(child);
            }
        }

        foreach (NcxNavPoint navPoint in NavMap)
        {
            WalkNavPoint(navPoint);
        }

        foreach (NcxPageTarget target in PageList)
        {
            target.PlayOrder = order++;
        }

        foreach (NcxNavList navList in NavLists)
        {
            foreach (NcxNavTarget target in navList.Targets)
            {
                target.PlayOrder = order++;
            }
        }
    }

    /// <summary>
    /// Serializes the whole NCX document in a fixed format: an XML declaration with <c>encoding="utf-8"</c>,
    /// an optional DOCTYPE, 2-space indentation, <c>\n</c> line endings. <c>playOrder</c> is recalculated
    /// (<see cref="RecomputePlayOrder"/>). The <c>pageList</c> / <c>navList</c> sections are omitted when empty.
    /// </summary>
    public string ToXml()
    {
        RecomputePlayOrder();

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        if (IncludeDoctype)
        {
            sb.Append(DoctypeLine).Append('\n');
        }

        sb.Append("<ncx xmlns=\"").Append(NcxNamespace).Append("\" version=\"").Append(Version).Append('"');
        foreach (KeyValuePair<string, string> pair in NcxExtraAttributes)
        {
            sb.Append(' ').Append(pair.Key).Append("=\"").Append(Attr(pair.Value)).Append('"');
        }

        sb.Append(">\n");

        sb.Append("  <head>\n");
        foreach (NcxMeta meta in Head)
        {
            sb.Append("    <meta name=\"").Append(Attr(meta.Name)).Append("\" content=\"").Append(Attr(meta.Content)).Append('"');
            if (!string.IsNullOrEmpty(meta.Scheme))
            {
                sb.Append(" scheme=\"").Append(Attr(meta.Scheme)).Append('"');
            }

            sb.Append("/>\n");
        }

        sb.Append("  </head>\n");

        sb.Append("  <docTitle>\n    <text>").Append(Text(DocTitle)).Append("</text>\n  </docTitle>\n");
        foreach (string author in DocAuthors)
        {
            sb.Append("  <docAuthor>\n    <text>").Append(Text(author)).Append("</text>\n  </docAuthor>\n");
        }

        sb.Append("  <navMap>\n");
        foreach (NcxNavPoint navPoint in NavMap)
        {
            AppendNavPoint(sb, navPoint, 4);
        }

        sb.Append("  </navMap>\n");

        if (PageList.Count > 0)
        {
            sb.Append("  <pageList>\n");
            foreach (NcxPageTarget target in PageList)
            {
                AppendPageTarget(sb, target);
            }

            sb.Append("  </pageList>\n");
        }

        foreach (NcxNavList navList in NavLists)
        {
            AppendNavList(sb, navList);
        }

        sb.Append("</ncx>\n");
        return sb.ToString();
    }

    private static void AppendNavPoint(StringBuilder sb, NcxNavPoint navPoint, int indent)
    {
        string pad = new(' ', indent);
        string id = string.IsNullOrEmpty(navPoint.Id)
            ? "navPoint-" + navPoint.PlayOrder.ToString(CultureInfo.InvariantCulture)
            : navPoint.Id;

        sb.Append(pad).Append("<navPoint id=\"").Append(Attr(id)).Append("\" playOrder=\"")
            .Append(navPoint.PlayOrder.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (!string.IsNullOrEmpty(navPoint.Class))
        {
            sb.Append(" class=\"").Append(Attr(navPoint.Class)).Append('"');
        }

        sb.Append(">\n");
        sb.Append(pad).Append("  <navLabel>\n").Append(pad).Append("    <text>").Append(Text(navPoint.Label))
            .Append("</text>\n").Append(pad).Append("  </navLabel>\n");
        sb.Append(pad).Append("  <content src=\"").Append(Attr(navPoint.ContentSrc)).Append("\"/>\n");

        foreach (NcxNavPoint child in navPoint.Children)
        {
            AppendNavPoint(sb, child, indent + 2);
        }

        sb.Append(pad).Append("</navPoint>\n");
    }

    private static void AppendPageTarget(StringBuilder sb, NcxPageTarget target)
    {
        sb.Append("    <pageTarget");
        if (!string.IsNullOrEmpty(target.Id))
        {
            sb.Append(" id=\"").Append(Attr(target.Id)).Append('"');
        }

        if (!string.IsNullOrEmpty(target.Type))
        {
            sb.Append(" type=\"").Append(Attr(target.Type)).Append('"');
        }

        if (!string.IsNullOrEmpty(target.Value))
        {
            sb.Append(" value=\"").Append(Attr(target.Value)).Append('"');
        }

        sb.Append(" playOrder=\"").Append(target.PlayOrder.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (!string.IsNullOrEmpty(target.Class))
        {
            sb.Append(" class=\"").Append(Attr(target.Class)).Append('"');
        }

        sb.Append(">\n");
        sb.Append("      <navLabel>\n        <text>").Append(Text(target.Label)).Append("</text>\n      </navLabel>\n");
        sb.Append("      <content src=\"").Append(Attr(target.ContentSrc)).Append("\"/>\n");
        sb.Append("    </pageTarget>\n");
    }

    private static void AppendNavList(StringBuilder sb, NcxNavList navList)
    {
        sb.Append("  <navList");
        if (!string.IsNullOrEmpty(navList.Id))
        {
            sb.Append(" id=\"").Append(Attr(navList.Id)).Append('"');
        }

        if (!string.IsNullOrEmpty(navList.Class))
        {
            sb.Append(" class=\"").Append(Attr(navList.Class)).Append('"');
        }

        sb.Append(">\n");
        sb.Append("    <navLabel>\n      <text>").Append(Text(navList.Label)).Append("</text>\n    </navLabel>\n");

        foreach (NcxNavTarget target in navList.Targets)
        {
            sb.Append("    <navTarget");
            if (!string.IsNullOrEmpty(target.Id))
            {
                sb.Append(" id=\"").Append(Attr(target.Id)).Append('"');
            }

            sb.Append(" playOrder=\"").Append(target.PlayOrder.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (!string.IsNullOrEmpty(target.Class))
            {
                sb.Append(" class=\"").Append(Attr(target.Class)).Append('"');
            }

            if (!string.IsNullOrEmpty(target.Value))
            {
                sb.Append(" value=\"").Append(Attr(target.Value)).Append('"');
            }

            sb.Append(">\n");
            sb.Append("      <navLabel>\n        <text>").Append(Text(target.Label)).Append("</text>\n      </navLabel>\n");
            sb.Append("      <content src=\"").Append(Attr(target.ContentSrc)).Append("\"/>\n");
            sb.Append("    </navTarget>\n");
        }

        sb.Append("  </navList>\n");
    }

    private static string Attr(string value) => Utility.EncodeXml(value);

    private static string Text(string value) => Utility.EncodeXml(value);

    private static int MaxDepth(IList<NcxNavPoint> nodes, int level)
    {
        int max = 0;
        foreach (NcxNavPoint node in nodes)
        {
            int depth = node.Children.Count > 0 ? MaxDepth(node.Children, level + 1) : level;
            if (depth > max)
            {
                max = depth;
            }
        }

        return max;
    }

    private static string CollapseWhitespace(string value)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private string GetHeadMeta(string name)
    {
        foreach (NcxMeta meta in Head)
        {
            if (string.Equals(meta.Name, name, StringComparison.Ordinal))
            {
                return meta.Content;
            }
        }

        return string.Empty;
    }

    private void SetHeadMeta(string name, string content)
    {
        foreach (NcxMeta meta in Head)
        {
            if (string.Equals(meta.Name, name, StringComparison.Ordinal))
            {
                meta.Content = content;
                return;
            }
        }

        Head.Insert(0, new NcxMeta(name, content));
    }

    private void ParseInternal(string source)
    {
        IncludeDoctype = source.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);

        MarkupTokenizer tokenizer = new(source);
        Stack<NcxNavPoint> navStack = new();
        NcxPageTarget? currentPage = null;
        NcxNavList? currentNavList = null;
        NcxNavTarget? currentNavTarget = null;

        TextSink sink = TextSink.None;
        bool capturing = false;
        StringBuilder pending = new();

        while (true)
        {
            MarkupToken? token = tokenizer.ParseNext();
            if (token is null)
            {
                break;
            }

            if (token.Text.Length > 0)
            {
                if (capturing)
                {
                    pending.Append(token.Text);
                }

                continue;
            }

            string name = token.TagName;
            if (name.Length == 0 || token.TagType == MarkupTokenType.None)
            {
                continue;
            }

            bool beginOrSingle = token.TagType is MarkupTokenType.Begin or MarkupTokenType.Single;

            if (beginOrSingle)
            {
                switch (name)
                {
                    case "ncx":
                        Version = token.Attributes.Value("version", "2005-1");
                        NcxExtraAttributes = CopyAttributesExcept(token.Attributes, "xmlns", "version");
                        break;

                    case "meta":
                        if (token.TagPath.Contains(".head", StringComparison.Ordinal))
                        {
                            Head.Add(new NcxMeta(
                                token.Attributes.Value("name"),
                                Utility.DecodeXml(token.Attributes.Value("content")),
                                token.Attributes.Value("scheme")));
                        }

                        break;

                    case "docAuthor":
                        DocAuthors.Add(string.Empty);
                        break;

                    case "navPoint":
                    {
                        NcxNavPoint navPoint = new()
                        {
                            Id = token.Attributes.Value("id"),
                            Class = token.Attributes.Value("class"),
                            PlayOrder = ParseInt(token.Attributes.Value("playOrder")),
                        };

                        if (navStack.Count > 0)
                        {
                            navStack.Peek().Children.Add(navPoint);
                        }
                        else
                        {
                            NavMap.Add(navPoint);
                        }

                        if (token.TagType == MarkupTokenType.Begin)
                        {
                            navStack.Push(navPoint);
                        }

                        break;
                    }

                    case "pageTarget":
                        currentPage = new NcxPageTarget
                        {
                            Id = token.Attributes.Value("id"),
                            Type = token.Attributes.Value("type"),
                            Value = token.Attributes.Value("value"),
                            Class = token.Attributes.Value("class"),
                            PlayOrder = ParseInt(token.Attributes.Value("playOrder")),
                        };
                        PageList.Add(currentPage);
                        if (token.TagType == MarkupTokenType.Single)
                        {
                            currentPage = null;
                        }

                        break;

                    case "navList":
                        currentNavList = new NcxNavList
                        {
                            Id = token.Attributes.Value("id"),
                            Class = token.Attributes.Value("class"),
                        };
                        NavLists.Add(currentNavList);
                        break;

                    case "navTarget":
                        currentNavTarget = new NcxNavTarget
                        {
                            Id = token.Attributes.Value("id"),
                            Class = token.Attributes.Value("class"),
                            Value = token.Attributes.Value("value"),
                            PlayOrder = ParseInt(token.Attributes.Value("playOrder")),
                        };
                        currentNavList?.Targets.Add(currentNavTarget);
                        if (token.TagType == MarkupTokenType.Single)
                        {
                            currentNavTarget = null;
                        }

                        break;

                    case "content":
                    {
                        string src = Utility.DecodeXml(token.Attributes.Value("src"));
                        if (currentNavTarget is not null)
                        {
                            currentNavTarget.ContentSrc = src;
                        }
                        else if (currentPage is not null)
                        {
                            currentPage.ContentSrc = src;
                        }
                        else if (navStack.Count > 0)
                        {
                            navStack.Peek().ContentSrc = src;
                        }

                        break;
                    }

                    case "text":
                        sink = ResolveSink(token.TagPath, navStack, currentPage, currentNavList, currentNavTarget);
                        pending.Clear();
                        capturing = true;
                        break;

                    default:
                        break;
                }
            }
            else
            {
                // MarkupTokenType.End
                switch (name)
                {
                    case "text":
                        if (capturing)
                        {
                            AssignText(sink, CollapseWhitespace(Utility.DecodeXml(pending.ToString())), navStack, currentPage, currentNavList, currentNavTarget);
                            capturing = false;
                            sink = TextSink.None;
                        }

                        break;

                    case "navPoint":
                        if (navStack.Count > 0)
                        {
                            navStack.Pop();
                        }

                        break;

                    case "pageTarget":
                        currentPage = null;
                        break;

                    case "navTarget":
                        currentNavTarget = null;
                        break;

                    case "navList":
                        currentNavList = null;
                        currentNavTarget = null;
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private enum TextSink
    {
        None,
        DocTitle,
        DocAuthor,
        NavPointLabel,
        PageTargetLabel,
        NavListLabel,
        NavTargetLabel,
    }

    private static TextSink ResolveSink(
        string tagPath,
        Stack<NcxNavPoint> navStack,
        NcxPageTarget? currentPage,
        NcxNavList? currentNavList,
        NcxNavTarget? currentNavTarget)
    {
        if (tagPath.Contains(".docTitle", StringComparison.Ordinal))
        {
            return TextSink.DocTitle;
        }

        if (tagPath.Contains(".docAuthor", StringComparison.Ordinal))
        {
            return TextSink.DocAuthor;
        }

        if (currentNavTarget is not null)
        {
            return TextSink.NavTargetLabel;
        }

        if (currentPage is not null)
        {
            return TextSink.PageTargetLabel;
        }

        if (navStack.Count > 0)
        {
            return TextSink.NavPointLabel;
        }

        if (currentNavList is not null)
        {
            return TextSink.NavListLabel;
        }

        return TextSink.None;
    }

    private void AssignText(
        TextSink sink,
        string value,
        Stack<NcxNavPoint> navStack,
        NcxPageTarget? currentPage,
        NcxNavList? currentNavList,
        NcxNavTarget? currentNavTarget)
    {
        switch (sink)
        {
            case TextSink.DocTitle:
                DocTitle = value;
                break;

            case TextSink.DocAuthor:
                if (DocAuthors.Count > 0)
                {
                    DocAuthors[^1] = value;
                }

                break;

            case TextSink.NavPointLabel:
                if (navStack.Count > 0)
                {
                    navStack.Peek().Label = value;
                }

                break;

            case TextSink.PageTargetLabel:
                if (currentPage is not null)
                {
                    currentPage.Label = value;
                }

                break;

            case TextSink.NavTargetLabel:
                if (currentNavTarget is not null)
                {
                    currentNavTarget.Label = value;
                }

                break;

            case TextSink.NavListLabel:
                if (currentNavList is not null)
                {
                    currentNavList.Label = value;
                }

                break;

            case TextSink.None:
            default:
                break;
        }
    }

    private static TagAttributes CopyAttributesExcept(TagAttributes source, params string[] excluded)
    {
        TagAttributes result = new();
        foreach (KeyValuePair<string, string> pair in source)
        {
            if (Array.IndexOf(excluded, pair.Key) < 0)
            {
                result.Set(pair.Key, pair.Value);
            }
        }

        return result;
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
}
