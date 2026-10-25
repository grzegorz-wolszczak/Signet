using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>
/// An attribute whose value an element already has from an ancestor, so writing it again changes nothing
/// (<see cref="InheritedAttributeCleaner.Find"/>).
/// </summary>
/// <param name="TagPos">The 0-based offset of the opening (or self-closing) tag the attribute belongs to.</param>
/// <param name="Pos">The offset of the attribute (<c>lang="pl-PL"</c>) in the text.</param>
/// <param name="Len">The length of the attribute.</param>
/// <param name="Name">The attribute name as written (<c>lang</c>, <c>xml:lang</c>, <c>dir</c>, <c>xmlns</c>…).</param>
/// <param name="Value">The value, with character references decoded.</param>
/// <param name="TagName">The element name in lower case.</param>
/// <param name="OpenTag">The text of the opening tag (for the report).</param>
/// <param name="FromTagName">The element the same value comes from (the nearest ancestor that sets it).</param>
internal sealed record RedundantAttribute(
    int TagPos,
    int Pos,
    int Len,
    string Name,
    string Value,
    string TagName,
    string OpenTag,
    string FromTagName);

/// <summary>
/// "Remove attributes that only repeat what is inherited": finds the attributes that carry the value the element
/// already has from the nearest ancestor setting it, and removes them from the source text
/// (<see cref="TagLister"/>), leaving the rest of the tag as it was. Whether a stylesheet still reaches the element
/// through such an attribute is decided by <see cref="InheritedAttributeRiskAnalyzer"/>.
/// </summary>
/// <remarks>
/// Only attributes that really inherit down the tree are considered:
/// <list type="bullet">
/// <item><c>lang</c> and <c>xml:lang</c> — the language of an element is the one of its nearest ancestor that
/// states it. <c>:lang()</c> selectors go on working after the removal (they test the language in effect, not the
/// attribute), only attribute selectors such as <c>[lang]</c> stop matching — that is what the risk analysis is
/// for. The two are weighed one by one: an element where they disagree keeps the one that disagrees.</item>
/// <item><c>dir</c> — the writing direction inherits the same way. <c>dir="auto"</c> never counts as redundant: it
/// works out the direction from the element's <b>own</b> text, so under another <c>auto</c> it can come out
/// different.</item>
/// <item>namespace declarations (<c>xmlns</c>, <c>xmlns:epub</c>…) — they are in force for the whole subtree, so
/// declaring the same URI again on a descendant says nothing. Compared exactly, a URI being case-sensitive.</item>
/// </list>
/// Attributes that do not inherit (<c>class</c>, <c>style</c>, <c>id</c>, <c>title</c>, <c>role</c>,
/// <c>epub:type</c>, <c>aria-*</c>) are never touched, however often they repeat.
/// </remarks>
internal static class InheritedAttributeCleaner
{
    // The attributes that inherit; a namespace declaration is recognized by its name (xmlns, xmlns:prefix).
    private static readonly string[] InheritedNames = ["lang", "xml:lang", "dir"];

    // A namespace prefix declared on a tag: xmlns:epub="…".
    private static readonly Regex NamespacePrefixDeclaration = new(
        @"(?<name>xmlns:[A-Za-z_][\w.\-]*)\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// All attributes of <paramref name="text"/> that only repeat an inherited value, in document order. The whole
    /// document is walked, not only the body: <c>&lt;body&gt;</c> repeating the language of <c>&lt;html&gt;</c> is
    /// exactly the case this looks for.
    /// </summary>
    public static IReadOnlyList<RedundantAttribute> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = TagLister.For(text);

        List<RedundantAttribute> found = new();

        // One frame per open element: what is in force inside it (name -> value and the element that set it). The
        // frame of an element is its parent's, plus what the element itself states.
        List<Dictionary<string, (string Value, string TagName)>> stack = new()
        {
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase),
        };

        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Pos < 0)
            {
                continue;
            }

            if (tag.Kind == TagKind.End)
            {
                if (stack.Count > 1)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            if (tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
            {
                continue;
            }

            string openTag = text.Substring(tag.Pos, tag.Len);
            Dictionary<string, (string Value, string TagName)> inherited = stack[^1];
            Dictionary<string, (string Value, string TagName)>? own = null;

            List<RedundantAttribute> here = new();
            foreach (string name in NamesIn(openTag))
            {
                TagLister.AttInfo attribute = TagLister.ParseAttribute(openTag, name);
                if (attribute.Pos < 0)
                {
                    continue;
                }

                string value = WebUtility.HtmlDecode(attribute.AValue).Trim();
                if (inherited.TryGetValue(name, out (string Value, string TagName) from) && IsSameValue(name, from.Value, value))
                {
                    here.Add(new RedundantAttribute(
                        tag.Pos,
                        tag.Pos + attribute.Pos,
                        attribute.Len,
                        name,
                        value,
                        tag.TagName.ToLowerInvariant(),
                        openTag,
                        from.TagName));
                }

                own ??= new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                own[name] = (value, tag.TagName.ToLowerInvariant());
            }

            // The attributes of one tag in the order they are written there, whatever order they were looked for in.
            found.AddRange(here.OrderBy(a => a.Pos));

            if (tag.Kind == TagKind.SelfClosing)
            {
                continue;
            }

            if (own is null)
            {
                stack.Add(inherited);
                continue;
            }

            Dictionary<string, (string Value, string TagName)> child = new(inherited, StringComparer.OrdinalIgnoreCase);
            foreach ((string name, (string Value, string TagName) entry) in own)
            {
                child[name] = entry;
            }

            stack.Add(child);
        }

        return found;
    }

    /// <summary>
    /// Removes the attributes of <paramref name="text"/> named by <paramref name="attributes"/> (the
    /// <see cref="RedundantAttribute.TagPos"/> and <see cref="RedundantAttribute.Name"/> pairs found in the same
    /// text), together with the white space before each of them.
    /// </summary>
    public static string Remove(string text, IEnumerable<(int TagPos, string Name)> attributes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(attributes);
        HashSet<(int, string)> wanted = attributes.ToHashSet();
        foreach (RedundantAttribute attribute in Find(text)
                     .Where(a => wanted.Contains((a.TagPos, a.Name)))
                     .OrderByDescending(a => a.Pos))
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

    /// <summary>Whether <paramref name="name"/> is an attribute this step may remove.</summary>
    public static bool IsInheritedAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return IsNamespaceDeclaration(name) || InheritedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsNamespaceDeclaration(string name) =>
        name.Equals("xmlns", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("xmlns:", StringComparison.OrdinalIgnoreCase);

    // The names this tag may be asked about: the fixed ones, plus the namespace prefixes it declares. The names of
    // the prefixes are the only part that cannot be known beforehand; a name the regex picks out of a value instead
    // of out of an attribute is simply not found by TagLister.ParseAttribute, which compares whole names.
    private static IEnumerable<string> NamesIn(string openTag)
    {
        foreach (string name in InheritedNames)
        {
            yield return name;
        }

        yield return "xmlns";

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in NamespacePrefixDeclaration.Matches(openTag))
        {
            string name = match.Groups["name"].Value;
            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }

    // Two values mean the same thing: language tags and directions do not care about case (BCP 47), a namespace
    // URI does. An empty value never counts: lang="" deliberately says "no language", whatever the ancestor says.
    private static bool IsSameValue(string name, string inherited, string own)
    {
        if (own.Length == 0 || inherited.Length == 0)
        {
            return false;
        }

        if (IsNamespaceDeclaration(name))
        {
            return string.Equals(inherited, own, StringComparison.Ordinal);
        }

        // dir="auto" works the direction out from the element's own text, so it is never the same as what it inherits.
        if (name.Equals("dir", StringComparison.OrdinalIgnoreCase) &&
            own.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(inherited, own, StringComparison.OrdinalIgnoreCase);
    }
}
