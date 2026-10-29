using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

/// <summary>A class name in a <c>class="…"</c> attribute and the offset of its first character in the file text.</summary>
internal readonly record struct ClassAttributeToken(string Name, int Offset);

/// <summary>
/// Finds and removes class names in the <c>class</c> attributes of an XHTML text, token by token, without parsing the
/// document (the edits are offsets into the original text). Shared by <see cref="ClassRemover"/> and the
/// undefined-classes step of <see cref="CleanupAnalysis"/>.
/// </summary>
internal static class ClassAttributeEditor
{
    /// <summary>Every class name of every <c>class</c> attribute of <paramref name="text"/>, in text order.</summary>
    public static IReadOnlyList<ClassAttributeToken> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<ClassAttributeToken> found = new();
        foreach ((int pos, _, TagLister.AttInfo attribute) in ClassAttributes(text))
        {
            foreach ((int start, int length) in Tokens(attribute.AValue))
            {
                found.Add(new ClassAttributeToken(attribute.AValue.Substring(start, length), pos + attribute.VPos + start));
            }
        }

        return found;
    }

    /// <summary>
    /// Adds to <paramref name="edits"/> the edits removing <paramref name="classNames"/> from the <c>class</c> attributes of
    /// <paramref name="text"/>: the value is rewritten without them, or the whole attribute goes when nothing is left.
    /// </summary>
    /// <returns>The number of class tokens removed.</returns>
    public static int AddRemovalEdits(
        List<(int Start, int End, string? Replacement)> edits,
        string text,
        IReadOnlySet<string> classNames)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(classNames);
        if (classNames.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        foreach ((int pos, int len, TagLister.AttInfo attribute) in ClassAttributes(text))
        {
            List<string> tokens = Tokens(attribute.AValue).Select(t => attribute.AValue.Substring(t.Start, t.Length)).ToList();
            int matched = tokens.RemoveAll(classNames.Contains);
            if (matched == 0)
            {
                continue;
            }

            removed += matched;
            if (tokens.Count == 0)
            {
                // Remove the whole "class='…'" attribute, plus one adjacent whitespace character.
                int attrStart = pos + attribute.Pos;
                int attrEnd = attrStart + attribute.Len;
                if (attrStart > pos && char.IsWhiteSpace(text[attrStart - 1]))
                {
                    attrStart--;
                }
                else if (attrEnd < pos + len && char.IsWhiteSpace(text[attrEnd]))
                {
                    attrEnd++;
                }

                edits.Add((attrStart, attrEnd, null));
            }
            else
            {
                int valueStart = pos + attribute.VPos;
                edits.Add((valueStart, valueStart + attribute.AValue.Length, string.Join(" ", tokens)));
            }
        }

        return removed;
    }

    /// <summary><paramref name="text"/> without <paramref name="classNames"/> in its <c>class</c> attributes.</summary>
    public static string Remove(string text, IReadOnlySet<string> classNames)
    {
        List<(int Start, int End, string? Replacement)> edits = new();
        AddRemovalEdits(edits, text, classNames);
        return ApplyEdits(text, edits);
    }

    /// <summary>Applies non-overlapping edits (offsets into <paramref name="text"/>), the last one first.</summary>
    public static string ApplyEdits(string text, List<(int Start, int End, string? Replacement)> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);
        StringBuilder sb = new(text);
        foreach ((int start, int end, string? replacement) in edits.OrderByDescending(e => e.Start))
        {
            sb.Remove(start, end - start);
            if (replacement is not null)
            {
                sb.Insert(start, replacement);
            }
        }

        return sb.ToString();
    }

    // The opening tags with a non-empty class attribute: the tag's offset and length, and the attribute. Most tags have no class
    // at all, so a tag without the word is skipped before its attributes are parsed.
    private static IEnumerable<(int Pos, int Len, TagLister.AttInfo Attribute)> ClassAttributes(string text)
    {
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            if (!text.AsSpan(pos, len).Contains("class", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(pos, len), "class");
            if (attribute.Pos >= 0 && attribute.VLen > 0)
            {
                yield return (pos, len, attribute);
            }
        }
    }

    // The class names of an attribute value (split on whitespace): their start and length in the value.
    private static IEnumerable<(int Start, int Length)> Tokens(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && char.IsWhiteSpace(value[i]))
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i]))
            {
                i++;
            }

            if (i > start)
            {
                yield return (start, i - start);
            }
        }
    }
}
