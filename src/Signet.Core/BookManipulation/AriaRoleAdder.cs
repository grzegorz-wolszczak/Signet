using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

/// <summary>An element that has an <c>epub:type</c> with a matching ARIA role but no <c>role</c> (<see cref="AriaRoleAdder.Find"/>).</summary>
/// <param name="Offset">The offset of the opening tag.</param>
/// <param name="Length">The length of the opening tag.</param>
/// <param name="Line">The 1-based line of the opening tag.</param>
/// <param name="TagName">The element name in lower case.</param>
/// <param name="Role">The role to add (<c>doc-noteref</c>).</param>
internal sealed record MissingAriaRole(int Offset, int Length, int Line, string TagName, string Role);

/// <summary>
/// "Structural semantics without ARIA roles": screen readers use the <c>role</c> attribute, not <c>epub:type</c>, so a noteref link
/// without <c>role="doc-noteref"</c> is not announced as a link to a note. Finds the elements whose <c>epub:type</c> has a DPUB-ARIA role
/// in the EPUB Type to ARIA Role Authoring Guide (the table is Calibre's) and adds the role where it is allowed on the element.
/// </summary>
/// <remarks>
/// Only the DPUB-ARIA roles are used: the WAI-ARIA roles (<c>list</c>, <c>table</c>) change the nature of the element, the deprecated
/// <c>doc-endnote</c> and <c>doc-biblioentry</c> are not recommended, and <c>doc-cover</c> is for images, not for the cover type. A role
/// the author gave (even a different one) is left alone. The <c>role</c> attribute is not valid in the XHTML 1.1 of EPUB 2, so the
/// caller checks EPUB 3 books only.
/// </remarks>
internal static class AriaRoleAdder
{
    private const string EpubTypeAttribute = "epub:type";
    private const string RoleAttribute = "role";

    // epub:type → role and the elements it is allowed on (besides the ones that accept any role).
    private static readonly Dictionary<string, (string Role, string[] Tags)> RoleForEpubType = BuildRoles();

    // Elements that accept any role (ARIA in HTML). Not included: <a> with an href, <figure> with a <figcaption> and table cells and
    // rows, which accept any role only in some circumstances.
    private static readonly HashSet<string> AcceptsAnyRole = new(
        ("abbr address b bdi bdo blockquote canvas cite code data del dfn div em hgroup i ins kbd mark output p pre q rp rt ruby s samp small "
        + "span strong sub sup table tbody tfoot thead time u var").Split(' '),
        StringComparer.Ordinal);

    /// <summary>The elements of <paramref name="text"/> that lack the role their <c>epub:type</c> asks for, in text order.</summary>
    public static IReadOnlyList<MissingAriaRole> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<MissingAriaRole> found = new();
        if (!text.Contains(EpubTypeAttribute, StringComparison.Ordinal))
        {
            return found;
        }

        TagLister lister = TagLister.For(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        int line = 1;
        int linePosition = 0;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
            {
                continue;
            }

            string tagText = text.Substring(tag.Pos, tag.Len);
            TagLister.AttInfo type = TagLister.ParseAttribute(tagText, EpubTypeAttribute);
            if (type.Pos < 0 || TagLister.ParseAttribute(tagText, RoleAttribute).AValue.Trim().Length > 0)
            {
                continue;
            }

            string name = tag.TagName.ToLowerInvariant();
            if (RoleFor(lister, i, name, tagText, type.AValue) is not { } role)
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

            found.Add(new MissingAriaRole(tag.Pos, tag.Len, line, name, role));
        }

        return found;
    }

    /// <summary>
    /// <paramref name="text"/> with the missing roles added (an empty <c>role</c> attribute gets its value); the same text when none is missing.
    /// </summary>
    public static string Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<MissingAriaRole> missing = Find(text);
        if (missing.Count == 0)
        {
            return text;
        }

        StringBuilder sb = new(text);
        foreach (MissingAriaRole role in missing.OrderByDescending(m => m.Offset))
        {
            string tagText = text.Substring(role.Offset, role.Length);
            TagLister.AttInfo existing = TagLister.ParseAttribute(tagText, RoleAttribute);
            if (existing.Pos >= 0)
            {
                sb.Remove(role.Offset + existing.VPos, existing.AValue.Length);
                sb.Insert(role.Offset + existing.VPos, role.Role);
            }
            else
            {
                sb.Insert(role.Offset + 1 + role.TagName.Length, $" {RoleAttribute}=\"{role.Role}\"");
            }
        }

        return sb.ToString();
    }

    // The role of the first epub:type of the element that has one valid on it.
    private static string? RoleFor(TagLister lister, int index, string name, string tagText, string epubType)
    {
        foreach (string type in epubType.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (RoleForEpubType.TryGetValue(type, out (string Role, string[] Tags) entry)
                && (Array.IndexOf(entry.Tags, name) >= 0 || AcceptsAny(lister, index, name, tagText)))
            {
                return entry.Role;
            }
        }

        return null;
    }

    private static bool AcceptsAny(TagLister lister, int index, string name, string tagText) => name switch
    {
        "a" => TagLister.ParseAttribute(tagText, "href").Pos < 0,
        "figure" => !HasDirectChild(lister, index, "figcaption"),
        _ => AcceptsAnyRole.Contains(name),
    };

    // Whether the element opened by tag index has a child element with the name (not just a descendant).
    private static bool HasDirectChild(TagLister lister, int index, string name)
    {
        int close = lister.FindCloseTagForOpen(index);
        int end = close < 0 ? lister.Count : close;
        int depth = 0;
        for (int i = index + 1; i < end; i++)
        {
            TagLister.TagInfo tag = lister.At(i);
            if (tag.Kind == TagKind.End)
            {
                depth--;
            }
            else if (tag.Kind is TagKind.Begin or TagKind.SelfClosing)
            {
                if (depth == 0 && tag.TagName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (tag.Kind == TagKind.Begin)
                {
                    depth++;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, (string Role, string[] Tags)> BuildRoles()
    {
        string[] sectioning = ["section"];
        string[] links = ["a"];
        Dictionary<string, (string Role, string[] Tags)> roles = new(StringComparer.Ordinal);
        foreach (string type in ("abstract acknowledgments afterword appendix bibliography chapter colophon conclusion credit credits dedication "
            + "endnotes epilogue errata foreword glossary introduction notice part preface prologue qna").Split(' '))
        {
            roles[type] = ("doc-" + type, sectioning);
        }

        foreach (string type in new[] { "backlink", "biblioref", "glossref", "noteref" })
        {
            roles[type] = ("doc-" + type, links);
        }

        roles["epigraph"] = ("doc-epigraph", []);
        roles["footnote"] = ("doc-footnote", ["aside", "footer", "header"]);
        roles["index"] = ("doc-index", ["nav", "section"]);
        roles["page-list"] = ("doc-pagelist", ["nav", "section"]);
        roles["pagebreak"] = ("doc-pagebreak", ["hr"]);
        roles["pullquote"] = ("doc-pullquote", ["aside", "section"]);
        roles["subtitle"] = ("doc-subtitle", ["h1", "h2", "h3", "h4", "h5", "h6"]);
        roles["tip"] = ("doc-tip", ["aside"]);
        roles["toc"] = ("doc-toc", ["nav", "section"]);
        return roles;
    }
}
