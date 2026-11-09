using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

/// <summary>An id named by an attribute that refers to elements of the same document (<see cref="IdRefAttributes.Tokens"/>).</summary>
/// <param name="Attribute">The attribute (<c>aria-labelledby</c>).</param>
/// <param name="Offset">The offset of the id in the opening tag text.</param>
/// <param name="Id">The id.</param>
internal readonly record struct IdRefToken(string Attribute, int Offset, string Id);

/// <summary>
/// The attributes whose value is a space separated list of ids of elements in the same document: the ARIA ones
/// (<c>aria-labelledby</c>, <c>aria-describedby</c>, <c>aria-controls</c>, <c>aria-owns</c>, <c>aria-flowto</c>, <c>aria-details</c>,
/// <c>aria-activedescendant</c>, <c>aria-errormessage</c>) on every element, <c>for</c> on <c>label</c> and <c>output</c>, <c>headers</c>
/// on <c>td</c> and <c>th</c> (the list of Calibre's <c>iteridrefs</c>). Used by the check of dangling references and by the renaming
/// of ids, so a rename leaves no reference behind.
/// </summary>
internal static class IdRefAttributes
{
    private static readonly string[] Aria =
    [
        "aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-flowto", "aria-details", "aria-activedescendant",
        "aria-errormessage",
    ];

    private static readonly string[] ForAttribute = [.. Aria, "for"];
    private static readonly string[] HeadersAttribute = [.. Aria, "headers"];

    /// <summary>The attributes of an element with the name <paramref name="tagName"/> that are lists of ids.</summary>
    public static IReadOnlyList<string> For(string tagName) => tagName.ToLowerInvariant() switch
    {
        "label" or "output" => ForAttribute,
        "td" or "th" => HeadersAttribute,
        _ => Aria,
    };

    /// <summary>The ids that the id list attributes of the opening tag <paramref name="tagText"/> name, with their offsets in it.</summary>
    public static IEnumerable<IdRefToken> Tokens(string tagText, string tagName)
    {
        if (!tagText.Contains("aria-", StringComparison.Ordinal) && !tagText.Contains("for", StringComparison.Ordinal)
            && !tagText.Contains("headers", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (string attribute in For(tagName))
        {
            TagLister.AttInfo info = TagLister.ParseAttribute(tagText, attribute);
            if (info.Pos < 0 || info.VPos < 0 || info.AValue.Length == 0)
            {
                continue;
            }

            int i = 0;
            string value = info.AValue;
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
                    yield return new IdRefToken(attribute, info.VPos + start, value[start..i]);
                }
            }
        }
    }

    /// <summary>The name of the element of an opening tag text (<c>&lt;p class="a"&gt;</c> gives <c>p</c>).</summary>
    public static string TagNameOf(string tagText)
    {
        int end = 1;
        while (end < tagText.Length && !char.IsWhiteSpace(tagText[end]) && tagText[end] is not ('>' or '/'))
        {
            end++;
        }

        return tagText[1..end];
    }

    /// <summary>The offsets in <paramref name="text"/> of the id <paramref name="id"/> in the id list attributes of its elements (whole ids only).</summary>
    public static List<int> Find(string text, string id)
    {
        List<int> offsets = new();
        if (!text.Contains(id, StringComparison.Ordinal))
        {
            return offsets;
        }

        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            if (!text.AsSpan(pos, len).Contains(id, StringComparison.Ordinal))
            {
                continue;
            }

            string tagText = text.Substring(pos, len);
            offsets.AddRange(Tokens(tagText, TagNameOf(tagText))
                .Where(t => string.Equals(t.Id, id, StringComparison.Ordinal))
                .Select(t => pos + t.Offset));
        }

        return offsets;
    }

    /// <summary>
    /// <paramref name="text"/> with the id <paramref name="oldId"/> replaced by <paramref name="newId"/> in the id list attributes of its
    /// elements (whole ids only).
    /// </summary>
    public static string Rename(string text, string oldId, string newId)
    {
        List<int> offsets = Find(text, oldId);
        if (offsets.Count == 0)
        {
            return text;
        }

        StringBuilder sb = new(text);
        foreach (int offset in offsets.OrderByDescending(o => o))
        {
            sb.Remove(offset, oldId.Length).Insert(offset, newId);
        }

        return sb.ToString();
    }
}
