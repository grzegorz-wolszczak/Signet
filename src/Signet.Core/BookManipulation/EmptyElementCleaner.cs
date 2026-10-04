using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>An empty <c>&lt;p&gt;</c>, <c>&lt;span&gt;</c> or <c>&lt;div&gt;</c> found by <see cref="EmptyElementCleaner.Find"/>.</summary>
/// <param name="Pos">The 0-based offset of the opening (or self-closing) tag.</param>
/// <param name="End">The offset right after the closing tag (or the self-closing tag).</param>
/// <param name="TagName">The element name in lower case (<c>p</c>, <c>span</c> or <c>div</c>).</param>
/// <param name="OpenTag">The text of the opening tag (for the report).</param>
/// <param name="Content">The whitespace between the tags (empty for a self-closing tag).</param>
internal sealed record EmptyElement(int Pos, int End, string TagName, string OpenTag, string Content);

/// <summary>
/// "Remove empty <c>&lt;p&gt;</c>/<c>&lt;span&gt;</c>/<c>&lt;div&gt;</c> tags": finds elements with no content or
/// only whitespace (<c>&lt;p class="x"&gt;&lt;/p&gt;</c>, <c>&lt;p/&gt;</c>, <c>&lt;div&gt; &lt;/div&gt;</c>) and
/// removes them from the source text (<see cref="TagLister"/>). <c>&amp;nbsp;</c>, comments or any child element
/// make an element non-empty. Elements with an <c>id</c> are never touched — they can be the target of a link or a
/// table of contents entry.
/// </summary>
/// <remarks>
/// <para>A block (<c>p</c>, <c>div</c>) that is alone on its line is removed with the line; otherwise only the
/// element goes. For a <c>span</c> only the tags are removed and its whitespace stays, because in running text it
/// can be the space between two words (<c>word&lt;span&gt; &lt;/span&gt;word</c>).</para>
/// <para>One pass: an element that becomes empty only after its empty children are removed is found the next
/// time (the Cleanup dialog re-analyses the book after every "Clean").</para>
/// </remarks>
internal static class EmptyElementCleaner
{
    private static readonly string[] Names = { "p", "span", "div" };

    private static readonly Regex IdAttributeRegex = new(@"\sid\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>All empty elements in <paramref name="text"/> (well-formed XHTML), in document order.</summary>
    public static IReadOnlyList<EmptyElement> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        List<EmptyElement> found = new();
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Pos < 0 || !Names.Contains(tag.TagName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string openTag = text.Substring(tag.Pos, tag.Len);
            if (IdAttributeRegex.IsMatch(openTag) || !lister.IsPositionInBody(tag.Pos))
            {
                continue;
            }

            string name = tag.TagName.ToLowerInvariant();
            if (tag.Kind == TagKind.SelfClosing)
            {
                found.Add(new EmptyElement(tag.Pos, tag.Pos + tag.Len, name, openTag, string.Empty));
            }
            else if (tag.Kind == TagKind.Begin && lister.FindCloseTagForOpen(i) == i + 1)
            {
                int contentStart = tag.Pos + tag.Len;
                TagLister.TagInfo close = tags[i + 1];
                string content = text[contentStart..close.Pos];
                // Only XML whitespace: a no-break space (U+00A0) is content.
                if (content.All(c => c is ' ' or '\t' or '\r' or '\n' or '\f'))
                {
                    found.Add(new EmptyElement(tag.Pos, close.Pos + close.Len, name, openTag, content));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Removes the empty elements of <paramref name="text"/> that start at <paramref name="positions"/> (the
    /// <see cref="EmptyElement.Pos"/> values found in the same text).
    /// </summary>
    public static string Remove(string text, IEnumerable<int> positions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(positions);
        HashSet<int> wanted = positions.ToHashSet();
        foreach (EmptyElement element in Find(text).Where(e => wanted.Contains(e.Pos)).OrderByDescending(e => e.Pos))
        {
            if (element.TagName == "span")
            {
                text = string.Concat(text.AsSpan(0, element.Pos), element.Content, text.AsSpan(element.End));
                continue;
            }

            (int start, int end) = LineRange(text, element.Pos, element.End);
            text = text.Remove(start, end - start);
        }

        return text;
    }

    // The whole line when the element is alone on it (its indentation and line break go too), else the element.
    private static (int Start, int End) LineRange(string text, int pos, int end)
    {
        int lineStart = pos;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        int lineEnd = end;
        while (lineEnd < text.Length && text[lineEnd] is ' ' or '\t')
        {
            lineEnd++;
        }

        bool startsLine = lineStart == 0 || text[lineStart - 1] == '\n';
        if (!startsLine || lineEnd >= text.Length || text[lineEnd] is not ('\r' or '\n'))
        {
            return (pos, end);
        }

        if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
        {
            lineEnd++;
        }

        return (lineStart, lineEnd + 1);
    }
}
