using System;
using System.Collections.Generic;
using AngleSharp.Dom;

namespace Signet.Core.Footnotes;

/// <summary>
/// The elements of a document by their id.
/// </summary>
/// <remarks>
/// AngleSharp keeps no index of ids: <c>IDocument.GetElementById</c> walks the tree. Asking it once per link
/// therefore cost the whole document every time, and a file holding thousands of footnotes paid for that in the
/// square of their number — it was the single most expensive step of scanning and of validating a book's footnotes.
/// This index is built once per document, in one pass, and answers the same question: the first element in document
/// order that carries the id.
/// </remarks>
internal static class ElementIds
{
    /// <summary>The elements of <paramref name="document"/> by their id, the first of a repeated id winning.</summary>
    public static Dictionary<string, IElement> Of(IDocument document)
    {
        Dictionary<string, IElement> byId = new(StringComparer.Ordinal);
        foreach (IElement element in document.All)
        {
            if (element.GetAttribute("id") is { Length: > 0 } id)
            {
                byId.TryAdd(id, element);
            }
        }

        return byId;
    }
}
