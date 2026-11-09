using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>What kind of image an <see cref="AccessibilityImage"/> is.</summary>
public enum AccessibilityImageKind
{
    /// <summary>An <c>&lt;img&gt;</c> element.</summary>
    Img,

    /// <summary>An inline <c>&lt;svg&gt;</c> element (the outermost one).</summary>
    Svg,
}

/// <summary>An image of the book and its text alternative, as the "Image descriptions" tab lists it.</summary>
/// <param name="BookPath">The (X)HTML file the image is in.</param>
/// <param name="Offset">The offset of the image's opening tag in the file.</param>
/// <param name="Line">The 1-based line of the opening tag.</param>
/// <param name="Kind">An <c>img</c> or an inline <c>svg</c>.</param>
/// <param name="Source">The book path of the image file of an <c>img</c> (for a preview); <c>null</c> for an inline svg or a remote image.</param>
/// <param name="Description">The text alternative a screen reader would use (<c>alt</c>; for an svg <c>aria-labelledby</c>, <c>aria-label</c>, <c>title</c>).</param>
/// <param name="IsDecorative">Whether the image is marked as decoration (<c>role="presentation"</c>, <c>aria-hidden</c>, an empty <c>alt</c>).</param>
/// <param name="Snippet">The opening tag on one line (for the list).</param>
public sealed record AccessibilityImage(
    string BookPath, int Offset, int Line, AccessibilityImageKind Kind, string? Source, string Description, bool IsDecorative, string Snippet)
{
    /// <summary>Whether the image has a text alternative.</summary>
    public bool HasDescription => Description.Length > 0;
}

/// <summary>What to write for an image (<see cref="AccessibilityImages.Apply"/>): its description, or that it is decoration.</summary>
/// <param name="BookPath">The file.</param>
/// <param name="Offset">The offset of the opening tag as <see cref="AccessibilityImages.List"/> gave it.</param>
/// <param name="Description">The text alternative; empty for none.</param>
/// <param name="Decorative">Whether the image is decoration (then <paramref name="Description"/> is not written).</param>
public sealed record ImageDescriptionEdit(string BookPath, int Offset, string Description, bool Decorative);

/// <summary>
/// The "Image descriptions" part of Tools → Accessibility: lists the images of the book in reading order with their text alternatives
/// and writes the ones the user edits (Calibre 81cae32b24, f3cba29a44). An <c>img</c> is described by <c>alt</c> (decoration: an empty
/// <c>alt</c> and <c>role="presentation"</c>); an inline <c>svg</c> by <c>aria-label</c> kept in step with its <c>&lt;title&gt;</c>
/// (decoration: <c>aria-hidden="true"</c>). Text that an <c>aria-labelledby</c> names is never changed: the reference is replaced.
/// Works on the source text (<see cref="TagLister"/>).
/// </summary>
public static class AccessibilityImages
{
    private static readonly Regex AnyTag = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WhiteSpaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The images of the book: the (X)HTML files in the order of the spine, then the others; within a file in text order.</summary>
    public static IReadOnlyList<AccessibilityImage> List(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        List<AccessibilityImage> images = new();
        foreach (HtmlResource html in InReadingOrder(book))
        {
            images.AddRange(Scan(html).Select(s => s.Image));
        }

        return images;
    }

    /// <summary>
    /// Writes the edits and returns how many images changed (an edit that changes nothing, or whose place is no longer the image it was,
    /// is skipped). Only the files that change are written.
    /// </summary>
    public static int Apply(Book book, IEnumerable<ImageDescriptionEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(edits);
        int changed = 0;
        foreach (IGrouping<string, ImageDescriptionEdit> file in edits.GroupBy(e => e.BookPath, StringComparer.Ordinal))
        {
            if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(file.Key) is not HtmlResource html)
            {
                continue;
            }

            string text = html.GetText();
            Dictionary<int, Scanned> byOffset = Scan(html).ToDictionary(s => s.Image.Offset);
            StringBuilder sb = new(text);
            foreach (ImageDescriptionEdit edit in file.OrderByDescending(e => e.Offset))
            {
                if (!byOffset.TryGetValue(edit.Offset, out Scanned? scanned))
                {
                    continue;
                }

                string original = text.Substring(scanned.Start, scanned.End - scanned.Start);
                string updated = scanned.Image.Kind == AccessibilityImageKind.Img ? EditImg(original, edit) : EditSvg(original, scanned, edit);
                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    sb.Remove(scanned.Start, scanned.End - scanned.Start).Insert(scanned.Start, updated);
                    changed++;
                }
            }

            ReferenceFix.SetIfChanged(html, text, sb.ToString());
        }

        return changed;
    }

    private sealed record Scanned(AccessibilityImage Image, int Start, int End, int OpenLength, IReadOnlyList<(int Start, int End, bool HasId)> Titles);

    private static List<HtmlResource> InReadingOrder(Book book)
    {
        FolderKeeper keeper = book.GetFolderKeeper();
        HtmlResource? nav = book.GetNavResource();
        List<HtmlResource> ordered = new();
        foreach (string bookPath in book.GetOpf().GetSpineOrderBookPaths())
        {
            if (keeper.GetResourceByBookPathNoThrow(bookPath) is HtmlResource html && !ReferenceEquals(html, nav) && !ordered.Contains(html))
            {
                ordered.Add(html);
            }
        }

        ordered.AddRange(book.GetHtmlResources().Where(h => !ReferenceEquals(h, nav) && !ordered.Contains(h)));
        return ordered;
    }

    // The images of one file: where each is, what it says.
    private static List<Scanned> Scan(HtmlResource html)
    {
        string text = html.GetText();
        List<Scanned> found = new();
        if (!text.Contains("<img", StringComparison.OrdinalIgnoreCase) && !text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return found;
        }

        TagLister lister = TagLister.For(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        Dictionary<string, string>? idTexts = null;
        int line = 1;
        int linePosition = 0;
        int svgEnd = -1;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind is not (TagKind.Begin or TagKind.SelfClosing) || tag.Pos < svgEnd)
            {
                continue;
            }

            bool isImg = tag.TagName.Equals("img", StringComparison.OrdinalIgnoreCase);
            bool isSvg = tag.TagName.Equals("svg", StringComparison.OrdinalIgnoreCase);
            if (!isImg && !isSvg)
            {
                continue;
            }

            for (; linePosition < tag.Pos; linePosition++)
            {
                if (text[linePosition] == '\n')
                {
                    line++;
                }
            }

            string open = text.Substring(tag.Pos, tag.Len);
            string snippet = WhiteSpaceRun.Replace(open, " ");
            bool hidden = AttributeValue(open, "aria-hidden").Equals("true", StringComparison.OrdinalIgnoreCase);
            string role = AttributeValue(open, "role");
            bool decorative = hidden || role is "presentation" or "none";
            if (isImg)
            {
                TagLister.AttInfo alt = TagLister.ParseAttribute(open, "alt");
                string description = alt.Pos >= 0 ? WebUtility.HtmlDecode(alt.AValue).Trim() : string.Empty;
                string src = WebUtility.HtmlDecode(AttributeValue(open, "src"));
                string? source = src.Length == 0 || src.Contains(':', StringComparison.Ordinal)
                    ? null
                    : BookPath.BuildBookPath(Utility.UrlDecodePath(src.Split('#', '?')[0]), html.Folder);
                found.Add(new Scanned(
                    new AccessibilityImage(html.BookPath, tag.Pos, line, AccessibilityImageKind.Img, source, description, decorative || (alt.Pos >= 0 && description.Length == 0), snippet),
                    tag.Pos, tag.Pos + tag.Len, tag.Len, []));
                continue;
            }

            int close = tag.Kind == TagKind.SelfClosing ? -1 : lister.FindCloseTagForOpen(i);
            int end = tag.Kind == TagKind.SelfClosing ? tag.Pos + tag.Len : close < 0 ? text.Length : tags[close].Pos + tags[close].Len;
            svgEnd = end;
            List<(int Start, int End, bool HasId)> titles = DirectTitles(text, tags, i, close);
            idTexts ??= IdTexts(text, lister);
            string svgDescription = SvgDescription(open, text, titles, idTexts);
            found.Add(new Scanned(
                new AccessibilityImage(html.BookPath, tag.Pos, line, AccessibilityImageKind.Svg, null, svgDescription, decorative, snippet),
                tag.Pos, end, tag.Len, titles));
        }

        return found;
    }

    // The title elements that are children of the svg (not of something nested in it): where they are and whether they have an id.
    private static List<(int Start, int End, bool HasId)> DirectTitles(string text, IReadOnlyList<TagLister.TagInfo> tags, int open, int close)
    {
        List<(int, int, bool)> titles = new();
        if (close < 0)
        {
            return titles;
        }

        int depth = 0;
        for (int i = open + 1; i < close; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind == TagKind.End)
            {
                depth--;
            }
            else if (tag.Kind is TagKind.Begin or TagKind.SelfClosing)
            {
                if (depth == 0 && tag.TagName.Equals("title", StringComparison.OrdinalIgnoreCase) && tag.Kind == TagKind.Begin)
                {
                    int titleClose = FindClose(tags, i, close);
                    if (titleClose > i)
                    {
                        bool hasId = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), "id").Pos >= 0;
                        titles.Add((tag.Pos, tags[titleClose].Pos + tags[titleClose].Len, hasId));
                    }
                }

                if (tag.Kind == TagKind.Begin)
                {
                    depth++;
                }
            }
        }

        return titles;
    }

    private static int FindClose(IReadOnlyList<TagLister.TagInfo> tags, int open, int limit)
    {
        int depth = 0;
        for (int i = open; i < limit; i++)
        {
            if (tags[i].Kind == TagKind.Begin)
            {
                depth++;
            }
            else if (tags[i].Kind == TagKind.End && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // The text of every element that has an id (for aria-labelledby).
    private static Dictionary<string, string> IdTexts(string text, TagLister lister)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Kind != TagKind.Begin)
            {
                continue;
            }

            string id = TagLister.ParseAttribute(text.Substring(tags[i].Pos, tags[i].Len), "id").AValue;
            int close = id.Length > 0 && !texts.ContainsKey(id) ? lister.FindCloseTagForOpen(i) : -1;
            if (close > i)
            {
                int from = tags[i].Pos + tags[i].Len;
                texts[id] = Clean(text[from..tags[close].Pos]);
            }
        }

        return texts;
    }

    private static string Clean(string markup) => WhiteSpaceRun.Replace(WebUtility.HtmlDecode(AnyTag.Replace(markup, " ")), " ").Trim();

    // What a screen reader says for an svg: aria-labelledby (the elements it names), else aria-label, else the title.
    private static string SvgDescription(string open, string text, List<(int Start, int End, bool HasId)> titles, Dictionary<string, string> idTexts)
    {
        string[] ids = AttributeValue(open, "aria-labelledby").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string named = string.Join(" ", ids.Where(idTexts.ContainsKey).Select(id => idTexts[id]).Where(t => t.Length > 0));
        if (named.Length > 0)
        {
            return named;
        }

        string label = WebUtility.HtmlDecode(AttributeValue(open, "aria-label")).Trim();
        if (label.Length > 0)
        {
            return label;
        }

        return titles.Select(t => Clean(text[t.Start..t.End])).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
    }

    private static string AttributeValue(string tag, string name)
    {
        TagLister.AttInfo info = TagLister.ParseAttribute(tag, name);
        return info.Pos >= 0 ? info.AValue.Trim() : string.Empty;
    }

    // --- writing ---

    private static string EditImg(string tag, ImageDescriptionEdit edit)
    {
        string description = edit.Description.Trim();
        if (edit.Decorative)
        {
            tag = SetAttribute(tag, "alt", string.Empty);
            return SetAttribute(tag, "role", "presentation");
        }

        tag = description.Length > 0 ? SetAttribute(tag, "alt", description) : RemoveAttribute(tag, "alt");
        return NotDecorative(tag);
    }

    private static string EditSvg(string element, Scanned scanned, ImageDescriptionEdit edit)
    {
        string open = element[..scanned.OpenLength];
        string rest = element[scanned.OpenLength..];
        string description = edit.Description.Trim();
        if (edit.Decorative)
        {
            return SetAttribute(open, "aria-hidden", "true") + rest;
        }

        open = NotDecorative(open);
        if (description.Length == 0)
        {
            open = RemoveAttribute(RemoveAttribute(open, "aria-label"), "aria-labelledby");
        }
        else
        {
            open = RemoveAttribute(SetAttribute(open, "aria-label", description), "aria-labelledby");
        }

        // The titles of the svg (ranges in the file, made relative to the element), from the last: the first one says the description, the
        // others with it; when the description goes, the titles that nothing can refer to by id go too.
        string encoded = WebUtility.HtmlEncode(description);
        List<(int Start, int End, bool HasId)> titles = scanned.Titles.Select(t => (t.Start - scanned.Start - scanned.OpenLength, t.End - scanned.Start - scanned.OpenLength, t.HasId)).ToList();
        for (int i = titles.Count - 1; i >= 0; i--)
        {
            (int start, int end, bool hasId) = titles[i];
            string title = rest[start..end];
            if (description.Length == 0)
            {
                if (!hasId)
                {
                    rest = rest.Remove(start, end - start);
                }
            }
            else if (i == 0)
            {
                int innerStart = title.IndexOf('>', StringComparison.Ordinal) + 1;
                int innerEnd = title.LastIndexOf("</", StringComparison.Ordinal);
                rest = rest.Remove(start, end - start).Insert(start, title[..innerStart] + encoded + title[innerEnd..]);
            }
        }

        return open + rest;
    }

    // Takes back what marks an image as decoration: a presentation role, aria-hidden="true".
    private static string NotDecorative(string tag)
    {
        if (AttributeValue(tag, "role") is "presentation" or "none")
        {
            tag = RemoveAttribute(tag, "role");
        }

        return AttributeValue(tag, "aria-hidden").Equals("true", StringComparison.OrdinalIgnoreCase) ? RemoveAttribute(tag, "aria-hidden") : tag;
    }

    // name="value" in the opening tag: the value replaced, or the attribute added at the end.
    private static string SetAttribute(string tag, string name, string value)
    {
        string attribute = $"{name}=\"{WebUtility.HtmlEncode(value)}\"";
        TagLister.AttInfo info = TagLister.ParseAttribute(tag, name);
        if (info.Pos >= 0)
        {
            return string.Equals(WebUtility.HtmlDecode(info.AValue), value, StringComparison.Ordinal)
                ? tag
                : tag[..info.Pos] + attribute + tag[(info.Pos + info.Len)..];
        }

        int end = tag.EndsWith("/>", StringComparison.Ordinal) ? tag.Length - 2 : tag.Length - 1;
        return tag[..end].TrimEnd() + " " + attribute + tag[end..];
    }

    private static string RemoveAttribute(string tag, string name)
    {
        TagLister.AttInfo info = TagLister.ParseAttribute(tag, name);
        if (info.Pos < 0)
        {
            return tag;
        }

        int start = info.Pos;
        int end = info.Pos + info.Len;
        if (start > 0 && char.IsWhiteSpace(tag[start - 1]))
        {
            start--;
        }

        return tag.Remove(start, end - start);
    }
}
