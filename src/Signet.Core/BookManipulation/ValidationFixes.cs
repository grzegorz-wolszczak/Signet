using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Replaces a reference (an <c>href</c>/<c>src</c> attribute of an (X)HTML file or a CSS <c>url()</c>) with another
/// one — the fix of a reference whose letter case differs from the real file name.
/// </summary>
public sealed class ReferenceFix : ValidationFix
{
    private readonly string _ownerBookPath;
    private readonly string _oldReference;
    private readonly string _newReference;

    /// <summary>Creates the fix.</summary>
    /// <param name="ownerBookPath">The file that holds the reference.</param>
    /// <param name="oldReference">The reference as it is now (decoded, as the validator saw it).</param>
    /// <param name="newReference">The corrected reference.</param>
    public ReferenceFix(string ownerBookPath, string oldReference, string newReference)
    {
        _ownerBookPath = ownerBookPath;
        _oldReference = oldReference;
        _newReference = newReference;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        Resource? owner = book.GetFolderKeeper().GetResourceByBookPathNoThrow(_ownerBookPath);
        if (owner is CssResource css)
        {
            Regex url = new(@"url\(\s*(['""]?)\s*" + Regex.Escape(_oldReference) + @"\s*\1\s*\)", RegexOptions.IgnoreCase);
            string text = css.GetText();
            string changed = url.Replace(text, m => $"url({m.Groups[1].Value}{_newReference}{m.Groups[1].Value})");
            return SetIfChanged(css, text, changed);
        }

        if (owner is HtmlResource html)
        {
            string text = html.GetText();
            string changed = MarkupEdits.ReplaceAttributeValues(
                text, MarkupEdits.LinkAttributes, value => value == _oldReference ? _newReference : null);
            return SetIfChanged(html, text, changed);
        }

        return false;
    }

    internal static bool SetIfChanged(TextResource resource, string text, string changed)
    {
        if (string.Equals(text, changed, StringComparison.Ordinal))
        {
            return false;
        }

        resource.SetText(changed);
        return true;
    }
}

/// <summary>Adds a file of the book that is missing from the OPF manifest to the manifest.</summary>
public sealed class AddToManifestFix : ValidationFix
{
    private readonly string _bookPath;

    /// <summary>Creates the fix for the file <paramref name="bookPath"/>.</summary>
    public AddToManifestFix(string bookPath) => _bookPath = bookPath;

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        OpfResource opf = book.GetOpf();
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not { } resource
            || LinkIntegrityValidator.BuildManifestBookPaths(book).Contains(_bookPath))
        {
            return false;
        }

        opf.AddResource(resource);
        return true;
    }
}

/// <summary>Sets the <c>media-type</c> of a manifest entry to the one its file extension calls for.</summary>
public sealed class ManifestMediaTypeFix : ValidationFix
{
    private readonly string _href;
    private readonly string _mediaType;

    /// <summary>Creates the fix of the manifest entry with <paramref name="href"/>.</summary>
    public ManifestMediaTypeFix(string href, string mediaType)
    {
        _href = href;
        _mediaType = mediaType;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        bool changed = false;
        foreach (ManifestEntry entry in document.Manifest.Where(e => e.Href == _href && e.MediaType != _mediaType))
        {
            entry.MediaType = _mediaType;
            changed = true;
        }

        if (changed)
        {
            opf.SetOpfDocument(document);
        }

        return changed;
    }
}

/// <summary>
/// Gives the book a unique identifier: <c>package/@unique-identifier</c> pointing at a <c>dc:identifier</c> with a
/// value (a new <c>urn:uuid:</c> when there is none).
/// </summary>
public sealed class UniqueIdentifierFix : ValidationFix
{
    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        string uid = document.Package.UniqueIdentifier;
        List<MetaEntry> identifiers = document.Metadata.Where(m => m.Name == "dc:identifier").ToList();
        MetaEntry? target = uid.Length > 0 ? identifiers.FirstOrDefault(m => m.Attributes.Value("id") == uid) : null;
        bool changed = false;

        if (target is null)
        {
            if (uid.Length == 0)
            {
                target = identifiers.FirstOrDefault(m => m.Attributes.Value("id").Length > 0);
                uid = target?.Attributes.Value("id") ?? "BookId";
            }

            if (target is null)
            {
                target = identifiers.FirstOrDefault(m => m.Attributes.Value("id").Length == 0);
                if (target is null)
                {
                    target = new MetaEntry { Name = "dc:identifier" };
                    document.Metadata.Insert(0, target);
                }

                target.Attributes.Set("id", uid);
            }

            document.Package.UniqueIdentifier = uid;
            changed = true;
        }

        if (target.Content.Trim().Length == 0)
        {
            target.Content = "urn:uuid:" + Utility.CreateUuid();
            changed = true;
        }

        if (changed)
        {
            opf.SetOpfDocument(document);
        }

        return changed;
    }
}

/// <summary>Wraps text placed directly in <c>&lt;body&gt;</c> in <c>&lt;p&gt;</c> elements (calibre's fix).</summary>
public sealed class BareBodyTextFix : ValidationFix
{
    private readonly string _bookPath;

    /// <summary>Creates the fix of the (X)HTML file <paramref name="bookPath"/>.</summary>
    public BareBodyTextFix(string bookPath) => _bookPath = bookPath;

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not HtmlResource html)
        {
            return false;
        }

        string text = html.GetText();
        return ReferenceFix.SetIfChanged(html, text, WrapBareBodyText(text));
    }

    /// <summary>Wraps every run of text that is a direct child of <c>&lt;body&gt;</c> in <c>&lt;p&gt;</c>.</summary>
    internal static string WrapBareBodyText(string text)
    {
        TagLister tags = new(text);
        int bodyOpen = tags.FindBodyOpenTag();
        if (bodyOpen < 0)
        {
            return text;
        }

        List<(int Start, int End)> runs = new();
        int depth = 0;
        for (int i = bodyOpen; i < tags.Count - 1; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (i > bodyOpen)
            {
                depth += tag.Kind switch { TagKind.Begin => 1, TagKind.End => -1, _ => 0 };
            }

            if (depth < 0)
            {
                break;
            }

            int start = tag.Pos + tag.Len;
            int end = tags.At(i + 1).Pos;
            if (depth == 0 && end > start && !string.IsNullOrWhiteSpace(text[start..end]))
            {
                runs.Add((start, end));
            }
        }

        StringBuilder result = new(text);
        foreach ((int start, int end) in runs.AsEnumerable().Reverse())
        {
            string run = text[start..end];
            string trimmed = run.Trim();
            int lead = run.IndexOf(trimmed, StringComparison.Ordinal);
            result.Remove(start, end - start)
                .Insert(start, run[..lead] + "<p>" + trimmed + "</p>" + run[(lead + trimmed.Length)..]);
        }

        return result.ToString();
    }
}

/// <summary>
/// Gives new ids to the second and later elements of an (X)HTML file that share an id; links keep pointing at the
/// first one, as they did (a reader goes to the first element with the id).
/// </summary>
public sealed class DuplicateIdFix : ValidationFix
{
    private readonly string _bookPath;
    private readonly string _id;

    /// <summary>Creates the fix of the id <paramref name="id"/> in the file <paramref name="bookPath"/>.</summary>
    public DuplicateIdFix(string bookPath, string id)
    {
        _bookPath = bookPath;
        _id = id;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not HtmlResource html)
        {
            return false;
        }

        string text = html.GetText();
        HashSet<string> ids = MarkupEdits.CollectIds(text);
        bool first = true;
        string changed = MarkupEdits.ReplaceAttributeValues(text, MarkupEdits.IdAttribute, value =>
        {
            if (value != _id)
            {
                return null;
            }

            if (first)
            {
                first = false;
                return null;
            }

            return MarkupEdits.UniqueId(_id, ids);
        });
        return ReferenceFix.SetIfChanged(html, text, changed);
    }
}

/// <summary>
/// Replaces an id that is not a valid XML name with a valid one: in the file, in every link to it (the (X)HTML files,
/// the NCX and the OPF guide) and in the id selectors of the stylesheets the file uses (calibre 9.16). A stylesheet
/// that another file with the same id also uses is left alone, so that file keeps its styling.
/// </summary>
public sealed class InvalidIdFix : ValidationFix
{
    private readonly string _bookPath;
    private readonly string _id;

    /// <summary>Creates the fix of the id <paramref name="id"/> in the file <paramref name="bookPath"/>.</summary>
    public InvalidIdFix(string bookPath, string id)
    {
        _bookPath = bookPath;
        _id = id;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not HtmlResource target)
        {
            return false;
        }

        string text = target.GetText();
        string newId = MarkupEdits.UniqueId(MarkupEdits.MakeValidId(_id), MarkupEdits.CollectIds(text));
        string renamed = MarkupEdits.ReplaceAttributeValues(text, MarkupEdits.IdAttribute, value => value == _id ? newId : null);
        if (!ReferenceFix.SetIfChanged(target, text, renamed))
        {
            return false;
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string source = html.GetText();
            ReferenceFix.SetIfChanged(html, source, MarkupEdits.ReplaceAttributeValues(
                source, MarkupEdits.LinkAttributes, value => RetargetFragment(value, html, target, newId)));
        }

        if (book.GetNcx() is { } ncx)
        {
            string source = ncx.GetText();
            ReferenceFix.SetIfChanged(ncx, source, MarkupEdits.ReplaceAttributeValues(
                source, MarkupEdits.SrcAttribute, value => RetargetFragment(value, ncx, target, newId)));
        }

        RetargetGuide(book.GetOpf(), target, newId);
        RenameInStylesheets(book, target, newId);
        return true;
    }

    // "file.xhtml#old" (pointing at the target) -> "file.xhtml#new"; null when the reference does not point at the id.
    private string? RetargetFragment(string reference, Resource owner, HtmlResource target, string newId)
    {
        int hash = reference.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || reference[(hash + 1)..] != _id)
        {
            return null;
        }

        string path = reference[..hash];
        bool pointsAtTarget = path.Length == 0
            ? ReferenceEquals(owner, target)
            : Core.BookPath.BuildBookPath(Utility.UrlDecodePath(path), owner.Folder) == target.BookPath;
        return pointsAtTarget ? path + "#" + newId : null;
    }

    private void RetargetGuide(OpfResource opf, HtmlResource target, string newId)
    {
        OpfDocument document = opf.GetOpfDocument();
        bool changed = false;
        foreach (GuideEntry entry in document.Guide)
        {
            if (RetargetFragment(entry.Href, opf, target, newId) is { } href)
            {
                entry.Href = href;
                changed = true;
            }
        }

        if (changed)
        {
            opf.SetOpfDocument(document);
        }
    }

    private void RenameInStylesheets(Book book, HtmlResource target, string newId)
    {
        string source = target.GetText();
        ReferenceFix.SetIfChanged(target, source, MarkupEdits.ReplaceInStyleElements(
            source, css => CssIdSelectors.Rename(css, _id, newId)));

        // Stylesheets that another file with the same id also uses stay as they are.
        HashSet<string> shared = new(StringComparer.Ordinal);
        foreach (HtmlResource other in book.GetHtmlResources())
        {
            if (!ReferenceEquals(other, target) && MarkupEdits.CollectIds(other.GetText()).Contains(_id))
            {
                shared.UnionWith(book.GetVisibleStylesheets(other));
            }
        }

        foreach (string cssBookPath in book.GetVisibleStylesheets(target).Where(p => !shared.Contains(p)))
        {
            if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(cssBookPath) is CssResource css)
            {
                string text = css.GetText();
                ReferenceFix.SetIfChanged(css, text, CssIdSelectors.Rename(text, _id, newId));
            }
        }
    }
}

/// <summary>Attribute-level edits of (X)HTML/XML text that keep the rest of the source as it is.</summary>
internal static class MarkupEdits
{
    /// <summary>The attributes that hold links.</summary>
    public static readonly string[] LinkAttributes = { "href", "src" };

    /// <summary>The <c>id</c> attribute.</summary>
    public static readonly string[] IdAttribute = { "id" };

    /// <summary>The <c>src</c> attribute (NCX links).</summary>
    public static readonly string[] SrcAttribute = { "src" };

    private static readonly Regex StyleElement = new(
        @"(<style\b[^>]*>)(.*?)(</style\s*>)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces the values of the given attributes in every opening tag: <paramref name="replace"/> gets the decoded
    /// value and returns the new (decoded) one, or <c>null</c> to keep it.
    /// </summary>
    public static string ReplaceAttributeValues(string text, IReadOnlyList<string> attributes, Func<string, string?> replace)
    {
        List<(int Start, int Length, string Value)> edits = new();
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            string tag = text.Substring(pos, len);
            foreach (string attribute in attributes)
            {
                TagLister.AttInfo info = TagLister.ParseAttribute(tag, attribute);
                if (info.Pos < 0 || info.VPos < 0)
                {
                    continue;
                }

                if (replace(WebUtility.HtmlDecode(info.AValue)) is { } value)
                {
                    char quote = info.VPos > 0 ? tag[info.VPos - 1] : '"';
                    edits.Add((pos + info.VPos, info.VLen, EncodeAttribute(value, quote)));
                }
            }
        }

        StringBuilder result = new(text);
        foreach ((int start, int length, string value) in edits.OrderByDescending(e => e.Start))
        {
            result.Remove(start, length).Insert(start, value);
        }

        return result.ToString();
    }

    /// <summary>Applies <paramref name="edit"/> to the content of every <c>&lt;style&gt;</c> element.</summary>
    public static string ReplaceInStyleElements(string text, Func<string, string> edit) =>
        StyleElement.Replace(text, m => m.Groups[1].Value + edit(m.Groups[2].Value) + m.Groups[3].Value);

    /// <summary>The contents of the <c>&lt;style&gt;</c> elements with their offsets in <paramref name="text"/>.</summary>
    public static IEnumerable<(int Offset, string Css)> StyleElements(string text) =>
        StyleElement.Matches(text).Select(m => (m.Groups[2].Index, m.Groups[2].Value));

    /// <summary>All id values of the file.</summary>
    public static HashSet<string> CollectIds(string text)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            TagLister.AttInfo info = TagLister.ParseAttribute(text.Substring(pos, len), "id");
            if (info.Pos >= 0)
            {
                ids.Add(WebUtility.HtmlDecode(info.AValue));
            }
        }

        return ids;
    }

    /// <summary><paramref name="id"/>, or <c>id_2</c>, <c>id_3</c>, … — the first one not in <paramref name="taken"/> (which gets it).</summary>
    public static string UniqueId(string id, HashSet<string> taken)
    {
        string candidate = id;
        for (int n = 2; taken.Contains(candidate); n++)
        {
            candidate = id + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        taken.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// A valid XML name made of <paramref name="id"/>: characters other than ASCII letters, digits, <c>_</c> and
    /// <c>-</c> become <c>_</c>, and an id that does not start with a letter or <c>_</c> gets the prefix <c>id_</c>.
    /// The result needs no escaping in a CSS selector either.
    /// </summary>
    public static string MakeValidId(string id)
    {
        StringBuilder result = new(id.Length + 3);
        foreach (char c in id.Trim())
        {
            result.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        if (result.Length == 0 || !(char.IsAsciiLetter(result[0]) || result[0] == '_'))
        {
            result.Insert(0, "id_");
        }

        return result.ToString();
    }

    /// <summary>Whether <paramref name="id"/> is a valid XML name without a colon (NCName).</summary>
    public static bool IsValidId(string id)
    {
        try
        {
            XmlConvert.VerifyNCName(id);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (ArgumentNullException)
        {
            return false;
        }
    }

    private static string EncodeAttribute(string value, char quote)
    {
        string encoded = value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        return quote == '\''
            ? encoded.Replace("'", "&apos;", StringComparison.Ordinal)
            : encoded.Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

/// <summary>Renames id selectors (<c>#name</c>) in CSS text, outside declaration blocks only.</summary>
internal static class CssIdSelectors
{
    private static readonly string[] RuleHoldingAtRules =
    {
        "@media", "@supports", "@document", "@-moz-document", "@layer", "@container", "@scope",
    };

    /// <summary>Replaces the id selector <paramref name="oldId"/> (also in its escaped form) with <paramref name="newId"/>.</summary>
    public static string Rename(string css, string oldId, string newId)
    {
        StringBuilder result = new(css.Length);
        Stack<bool> blocks = new(); // true = a declaration block, false = the block of an at-rule
        int i = 0;
        int preludeStart = 0;
        while (i < css.Length)
        {
            char c = css[i];
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? css.Length : end + 2;
                result.Append(css, i, end - i);
                i = end;
                continue;
            }

            if (c is '"' or '\'')
            {
                int end = i + 1;
                while (end < css.Length && css[end] != c)
                {
                    end += css[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end + 1, css.Length);
                result.Append(css, i, end - i);
                i = end;
                continue;
            }

            bool inDeclarations = blocks.Count > 0 && blocks.Peek();
            if (c == '{')
            {
                blocks.Push(!HoldsRules(css.AsSpan(preludeStart, i - preludeStart).TrimStart()));
                preludeStart = i + 1;
            }
            else if (c == '}')
            {
                if (blocks.Count > 0)
                {
                    blocks.Pop();
                }

                preludeStart = i + 1;
            }
            else if (c == ';' && !inDeclarations)
            {
                preludeStart = i + 1;
            }
            else if (c == '#' && !inDeclarations && TryReadIdent(css, i + 1, out string name, out int length) && name == oldId)
            {
                result.Append('#').Append(newId);
                i += 1 + length;
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    // Whether a block with this prelude holds rules (@media etc.) rather than declarations (a rule, @font-face, @page).
    private static bool HoldsRules(ReadOnlySpan<char> prelude)
    {
        foreach (string atRule in RuleHoldingAtRules)
        {
            if (prelude.StartsWith(atRule, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Reads a CSS identifier (with escapes) and returns it unescaped.
    private static bool TryReadIdent(string css, int start, out string name, out int length)
    {
        StringBuilder value = new();
        int i = start;
        while (i < css.Length)
        {
            char c = css[i];
            if (c == '\\' && i + 1 < css.Length)
            {
                int hex = 0;
                while (hex < 6 && i + 1 + hex < css.Length && Uri.IsHexDigit(css[i + 1 + hex]))
                {
                    hex++;
                }

                if (hex > 0)
                {
                    int codePoint = Convert.ToInt32(css.Substring(i + 1, hex), 16);
                    bool valid = codePoint is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);
                    value.Append(valid ? char.ConvertFromUtf32(codePoint) : "\uFFFD");
                    i += 1 + hex;
                    if (i < css.Length && char.IsWhiteSpace(css[i]))
                    {
                        i++;
                    }
                }
                else
                {
                    value.Append(css[i + 1]);
                    i += 2;
                }

                continue;
            }

            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || c > 0x7F)
            {
                value.Append(c);
                i++;
                continue;
            }

            break;
        }

        name = value.ToString();
        length = i - start;
        return length > 0;
    }
}
