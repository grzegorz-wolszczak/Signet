using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

internal readonly record struct ClassAttributeToken(string Name, int Offset);

internal static class ClassAttributeEditor
{
    public static IReadOnlyList<ClassAttributeToken> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<ClassAttributeToken> found = new();
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(pos, len), "class");
            if (attribute.Pos < 0 || attribute.VLen <= 0)
            {
                continue;
            }

            int i = 0;
            while (i < attribute.AValue.Length)
            {
                while (i < attribute.AValue.Length && char.IsWhiteSpace(attribute.AValue[i]))
                {
                    i++;
                }

                int start = i;
                while (i < attribute.AValue.Length && !char.IsWhiteSpace(attribute.AValue[i]))
                {
                    i++;
                }

                if (i > start)
                {
                    found.Add(new ClassAttributeToken(
                        attribute.AValue[start..i],
                        pos + attribute.VPos + start));
                }
            }
        }

        return found;
    }

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
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(pos, len), "class");
            if (attribute.Pos < 0 || attribute.VLen <= 0)
            {
                continue;
            }

            List<string> tokens = SplitClassTokens(attribute.AValue).ToList();
            int before = tokens.Count;
            tokens.RemoveAll(classNames.Contains);
            int matched = before - tokens.Count;
            if (matched == 0)
            {
                continue;
            }

            removed += matched;
            if (tokens.Count == 0)
            {
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

    public static string Remove(string text, IReadOnlySet<string> classNames)
    {
        List<(int Start, int End, string? Replacement)> edits = new();
        AddRemovalEdits(edits, text, classNames);
        return ApplyEdits(text, edits);
    }

    public static string ApplyEdits(string text, List<(int Start, int End, string? Replacement)> edits)
    {
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

    private static IEnumerable<string> SplitClassTokens(string value)
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
                yield return value[start..i];
            }
        }
    }
}
