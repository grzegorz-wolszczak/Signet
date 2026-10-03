using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// A single <c>h1</c>–<c>h6</c> heading found in XHTML source. The <see cref="Level"/>, <see cref="Title"/>,
/// <see cref="IncludeInToc"/>, <see cref="Id"/> and <see cref="Children"/> fields are mutable — they are modified by the
/// heading selection dialog (<c>HeadingSelectorModel</c>) before the table of contents is generated.
/// </summary>
/// <remarks>
/// The node is identified by <see cref="HeadingIndex"/> — its position among all the
/// <c>h1</c>–<c>h6</c> elements of the given file in document order. When the same
/// source is parsed again, the <c>h1,h2,h3,h4,h5,h6</c> selector returns those elements in the same order, so the index
/// unambiguously points at the same heading (changing the level rewrites the tag in place and does not change
/// the element's position).
/// </remarks>
public sealed class Heading
{
    /// <summary>The HTML resource the heading belongs to.</summary>
    public required HtmlResource ResourceFile { get; init; }

    /// <summary>The position among the file's <c>h1</c>–<c>h6</c> elements (document order, from 0).</summary>
    public required int HeadingIndex { get; init; }

    /// <summary>The text the heading shows in the TOC (the <c>title</c> attribute or, failing that, the element's text).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The current value of the <c>title</c> attribute (<c>""</c> when absent).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The value of the <c>title</c> attribute at load time — used to detect a change.</summary>
    public string OrigTitle { get; init; } = string.Empty;

    /// <summary>The heading level 1–6 (a smaller number = a "bigger" heading).</summary>
    public int Level { get; set; }

    /// <summary>The heading level at load time — used to detect a change.</summary>
    public int OrigLevel { get; init; }

    /// <summary>The value of the <c>id</c> attribute (<c>""</c> when absent).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The heading appears within <see cref="Headings.AllowedHeadingDistance"/> lines after the
    /// <c>&lt;body&gt;</c> tag (at most one such heading per file) — it "represents" the file and links
    /// to it without a fragment.
    /// </summary>
    public bool AtFileStart { get; init; }

    /// <summary>Whether the heading goes into the TOC (based on the <c>signet_not_in_toc</c> class).</summary>
    public bool IncludeInToc { get; set; } = true;

    /// <summary>The headings "below" this one in the hierarchy (a higher level number / smaller size).</summary>
    public List<Heading> Children { get; } = new();

    /// <summary>Set when the dialog changed something that has to be written to the document.</summary>
    public bool IsChanged { get; set; }
}

/// <summary>
/// Collecting <c>h1</c>–<c>h6</c> headings from XHTML sources and arranging them into a hierarchy.
/// </summary>
public static class Headings
{
    /// <summary>The class marking a heading excluded from the TOC (the current one).</summary>
    public const string SignetNotInTocClass = "signet_not_in_toc";

    /// <summary>
    /// The maximum distance (in lines) of a heading from the <c>&lt;body&gt;</c> tag at which the heading
    /// is still treated as the file's "name". The value was chosen arbitrarily.
    /// </summary>
    public const int AllowedHeadingDistance = 20;

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// A flat list of headings from all the given resources (resource order preserved,
    /// within a resource — document order).
    /// </summary>
    /// <param name="htmlResources">The resources to search (usually in spine order, without the nav).</param>
    /// <param name="includeUnwantedHeadings">Whether to include headings marked as excluded from the TOC.</param>
    public static IReadOnlyList<Heading> GetHeadingList(
        IEnumerable<HtmlResource> htmlResources,
        bool includeUnwantedHeadings = false)
    {
        ArgumentNullException.ThrowIfNull(htmlResources);
        List<Heading> list = new();
        foreach (HtmlResource resource in htmlResources)
        {
            list.AddRange(GetHeadingListForOneFile(resource, includeUnwantedHeadings));
        }

        return list;
    }

    /// <summary>The headings of a single resource (document order).</summary>
    public static IReadOnlyList<Heading> GetHeadingListForOneFile(
        HtmlResource htmlResource,
        bool includeUnwantedHeadings = false)
    {
        ArgumentNullException.ThrowIfNull(htmlResource);

        IHtmlDocument document = XhtmlDoc.Parse(htmlResource.GetText());

        int bodyLine = 0;
        if (document.QuerySelector("body")?.SourceReference is { } bodyReference)
        {
            bodyLine = bodyReference.Position.Line;
        }

        List<Heading> headings = new();
        IElement[] headingNodes = document.QuerySelectorAll("h1, h2, h3, h4, h5, h6").ToArray();

        for (int i = 0; i < headingNodes.Length; i++)
        {
            IElement node = headingNodes[i];

            string title = node.GetAttribute("title") ?? string.Empty;
            string id = node.GetAttribute("id") ?? string.Empty;
            string text = title.Length > 0 ? Simplified(title) : Simplified(node.TextContent);
            int level = node.LocalName[1] - '0';

            string classes = node.GetAttribute("class") ?? string.Empty;
            bool includeInToc = !classes.Contains(SignetNotInTocClass, StringComparison.Ordinal);

            int nodeLine = node.SourceReference?.Position.Line ?? 0;
            bool atFileStart = i == 0 && nodeLine - bodyLine < AllowedHeadingDistance;

            if (!includeInToc && !includeUnwantedHeadings)
            {
                continue;
            }

            headings.Add(new Heading
            {
                ResourceFile = htmlResource,
                HeadingIndex = i,
                Title = title,
                OrigTitle = title,
                Id = id,
                Text = text,
                Level = level,
                OrigLevel = level,
                IncludeInToc = includeInToc,
                AtFileStart = atFileStart,
            });
        }

        return headings;
    }

    /// <summary>
    /// Arranges a flat list into a hierarchy: headings of a higher level number following a
    /// given heading become its children. Operates on the
    /// passed objects (assumes their <see cref="Heading.Children"/> are empty).
    /// </summary>
    public static IReadOnlyList<Heading> MakeHeadingHierarchy(IReadOnlyList<Heading> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);
        List<Heading> ordered = headings.ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            while (i != ordered.Count - 1 && ordered[i + 1].Level > ordered[i].Level)
            {
                AddChildHeading(ordered[i], ordered[i + 1]);
                ordered.RemoveAt(i + 1);
            }
        }

        return ordered;
    }

    /// <summary>Flattens the hierarchy into a list (preorder).</summary>
    public static IReadOnlyList<Heading> GetFlattenedHeadings(IReadOnlyList<Heading> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);
        List<Heading> flat = new();
        foreach (Heading heading in headings)
        {
            FlattenHeadingNode(heading, flat);
        }

        return flat;
    }

    private static void FlattenHeadingNode(Heading heading, List<Heading> accumulator)
    {
        accumulator.Add(heading);
        foreach (Heading child in heading.Children)
        {
            FlattenHeadingNode(child, accumulator);
        }
    }

    private static void AddChildHeading(Heading parent, Heading newChild)
    {
        if (parent.Children.Count > 0 && parent.Children[^1].Level < newChild.Level)
        {
            AddChildHeading(parent.Children[^1], newChild);
        }
        else
        {
            parent.Children.Add(newChild);
        }
    }

    internal static string Simplified(string value) => WhitespaceRun.Replace(value, " ").Trim();
}
