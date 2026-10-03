using System;
using System.Collections.Generic;
using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;

namespace Signet.Core.Preview;

/// <summary>
/// An XHTML version enriched with <c>data-signet-loc</c> attributes on elements — the carrier of the position
/// mapping between the code editor and the preview (caret-location sync).
/// </summary>
/// <remarks>
/// Every element with an available source position gets a <c>data-signet-loc="{offset}"</c> attribute,
/// where <c>offset</c> is the 0-based character offset of its opening tag in the original
/// source (the same one the Code View caret uses). Code View to Preview direction:
/// <see cref="NearestLocAtOrBefore"/> returns the <c>loc</c> of the element to scroll to; Preview →
/// Code View direction: the page script sends back the <c>loc</c> of the clicked element, and the host sets
/// the caret at that offset.
/// </remarks>
public sealed class PreviewInstrumentation
{
    /// <summary>The name of the position marker attribute.</summary>
    public const string LocAttribute = "data-signet-loc";

    private readonly int[] _locs;

    private PreviewInstrumentation(string html, int[] sortedLocs)
    {
        Html = html;
        _locs = sortedLocs;
    }

    /// <summary>The instrumented XHTML document (to be written to the preview mirror).</summary>
    public string Html { get; }

    /// <summary>The source offsets, sorted ascending, that received a <c>data-signet-loc</c> marker.</summary>
    public IReadOnlyList<int> Locs => _locs;

    /// <summary>
    /// Instruments XHTML source. When parsing fails or no element has
    /// a source position, <see cref="Html"/> is the unchanged source and <see cref="Locs"/> is empty.
    /// </summary>
    /// <param name="xhtmlSource">The original source of the (X)HTML file.</param>
    public static PreviewInstrumentation Create(string xhtmlSource)
    {
        ArgumentNullException.ThrowIfNull(xhtmlSource);

        IHtmlDocument document;
        try
        {
            document = XhtmlDoc.Parse(xhtmlSource);
        }
        catch (Exception)
        {
            return new PreviewInstrumentation(xhtmlSource, Array.Empty<int>());
        }

        SortedSet<int> locs = new();

        foreach (IElement element in document.All)
        {
            int offset = XhtmlDoc.OffsetFromNode(element);
            if (offset < 0)
            {
                continue;
            }

            element.SetAttribute(LocAttribute, offset.ToString(CultureInfo.InvariantCulture));
            locs.Add(offset);
        }

        if (locs.Count == 0)
        {
            return new PreviewInstrumentation(xhtmlSource, Array.Empty<int>());
        }

        int[] sorted = new int[locs.Count];
        locs.CopyTo(sorted);
        return new PreviewInstrumentation(XhtmlDoc.Serialize(document), sorted);
    }

    /// <summary>
    /// The largest <c>loc</c> not greater than <paramref name="sourceOffset"/> (the element the
    /// Code View caret "sits" in), or <c>-1</c> when there is none.
    /// </summary>
    /// <param name="sourceOffset">The caret offset in the file source.</param>
    public int NearestLocAtOrBefore(int sourceOffset)
    {
        if (_locs.Length == 0 || sourceOffset < _locs[0])
        {
            return -1;
        }

        int index = Array.BinarySearch(_locs, sourceOffset);
        if (index >= 0)
        {
            return _locs[index];
        }

        // ~index is the index of the first greater element — step back by one.
        return _locs[~index - 1];
    }
}
