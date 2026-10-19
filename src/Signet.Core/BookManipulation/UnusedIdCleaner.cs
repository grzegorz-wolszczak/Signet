using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Signet.Core.BookManipulation;

/// <summary>An <c>id</c> attribute in the source text (<see cref="UnusedIdCleaner.Find"/>).</summary>
/// <param name="TagPos">The 0-based offset of the opening (or self-closing) tag the attribute belongs to.</param>
/// <param name="Pos">The offset of the attribute (<c>id="…"</c>) in the text.</param>
/// <param name="Len">The length of the attribute.</param>
/// <param name="Value">The id, with character references decoded.</param>
/// <param name="TagName">The element name in lower case.</param>
/// <param name="OpenTag">The text of the opening tag (for the report).</param>
internal sealed record IdAttribute(int TagPos, int Pos, int Len, string Value, string TagName, string OpenTag);

/// <summary>
/// "Remove unused <c>id</c> attributes": finds the <c>id</c> attributes of the elements in <c>&lt;body&gt;</c> and
/// removes them from the source text (<see cref="TagLister"/>), leaving the rest of the tag as it was. Whether anything
/// refers to an id is decided elsewhere (<see cref="IdReferenceIndex"/>), whether a stylesheet still reaches the
/// element by it by <see cref="UnusedIdRiskAnalyzer"/>.
/// </summary>
internal static class UnusedIdCleaner
{
    /// <summary>All non-empty <c>id</c> attributes in the <c>&lt;body&gt;</c> of <paramref name="text"/>, in document order.</summary>
    public static IReadOnlyList<IdAttribute> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = TagLister.For(text);
        List<IdAttribute> found = new();
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Pos < 0 || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing) || !lister.IsPositionInBody(tag.Pos))
            {
                continue;
            }

            string openTag = text.Substring(tag.Pos, tag.Len);
            TagLister.AttInfo id = TagLister.ParseAttribute(openTag, "id");
            if (id.Pos < 0 || id.AValue.Trim().Length == 0)
            {
                continue;
            }

            found.Add(new IdAttribute(
                tag.Pos, tag.Pos + id.Pos, id.Len, WebUtility.HtmlDecode(id.AValue.Trim()), tag.TagName.ToLowerInvariant(), openTag));
        }

        return found;
    }

    /// <summary>
    /// Removes the <c>id</c> attribute of every element of <paramref name="text"/> whose opening tag starts at one of
    /// <paramref name="tagPositions"/> (the <see cref="IdAttribute.TagPos"/> values found in the same text), together
    /// with the white space before it.
    /// </summary>
    public static string Remove(string text, IEnumerable<int> tagPositions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tagPositions);
        HashSet<int> wanted = tagPositions.ToHashSet();
        foreach (IdAttribute attribute in Find(text).Where(a => wanted.Contains(a.TagPos)).OrderByDescending(a => a.Pos))
        {
            int start = attribute.Pos;
            while (start > attribute.TagPos + 1 && text[start - 1] is ' ' or '\t' or '\r' or '\n' or '\f')
            {
                start--;
            }

            text = text.Remove(start, attribute.Pos + attribute.Len - start);
        }

        return text;
    }
}
