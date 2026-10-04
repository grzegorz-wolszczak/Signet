using System;
using System.Buffers;
using System.Collections.Generic;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using Signet.Core.BookManipulation;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Updates references to other files in the (X)HTML source after resources are renamed or moved.
/// Parsing is done with AngleSharp:
/// path attributes are recalculated through <see cref="HrefUpdate"/>, inline styles and the
/// contents of <c>&lt;style&gt;</c> through <see cref="PerformCssUpdates"/>.
/// </summary>
/// <remarks>
/// The document is reserialized as a whole,
/// so minor, cosmetic formatting differences outside the attributes that actually changed are
/// possible (normalization of quotes / empty elements by <see cref="XhtmlMarkupFormatter"/>).
/// The XML prolog and DOCTYPE are recreated with the same logic as <see cref="CleanSource.Mend"/>
/// (<see cref="CleanSource.BuildDoctype"/>); a document without a DOCTYPE stays without one.
/// </remarks>
public static class PerformHtmlUpdates
{
    // Attributes carrying a single path (without srcset, which has its own candidate list syntax),
    // plus `xlink:href` (SVG/MathML — the HTML5 parser does not put the attribute in a namespace, see XhtmlDoc).
    private static readonly string[] SinglePathAttributes =
    {
        "href", "src", "poster", "data", "xlink:href", "altimg",
    };

    private static readonly SearchValues<char> SrcsetCandidateSeparators = SearchValues.Create(" \t");

    /// <summary>
    /// Recalculates all references in <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The (X)HTML source.</param>
    /// <param name="oldBookPath">The bookpath of this file before the operation.</param>
    /// <param name="newBookPath">The bookpath of this file after the operation.</param>
    /// <param name="htmlUpdates">A map old→new bookpath for href/src references (all changed resources).</param>
    /// <param name="cssUpdates">The subset of <paramref name="htmlUpdates"/> relevant to styles (CSS/images/fonts/SVG).</param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> / <c>"3.0"</c>) — selects the DOCTYPE.</param>
    public static string Apply(
        string source,
        string oldBookPath,
        string newBookPath,
        IReadOnlyDictionary<string, string> htmlUpdates,
        IReadOnlyDictionary<string, string> cssUpdates,
        string version)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(htmlUpdates);
        ArgumentNullException.ThrowIfNull(cssUpdates);
        ArgumentNullException.ThrowIfNull(version);

        if (source.Length == 0)
        {
            return source;
        }

        string stripped = CleanSource.StripXmlDeclaration(source);
        HtmlParser parser = new(new HtmlParserOptions { IsKeepingSourceReferences = true });
        IDocument document = parser.ParseDocument(stripped);

        if (htmlUpdates.Count > 0)
        {
            foreach (IElement element in document.All)
            {
                foreach (string attributeName in SinglePathAttributes)
                {
                    string? value = element.GetAttribute(attributeName);
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    string updated = HrefUpdate.UpdateValue(value, htmlUpdates, oldBookPath, newBookPath);
                    if (!string.Equals(updated, value, StringComparison.Ordinal))
                    {
                        element.SetAttribute(attributeName, updated);
                    }
                }

                string? srcset = element.GetAttribute("srcset");
                if (!string.IsNullOrEmpty(srcset))
                {
                    string updated = UpdateSrcset(srcset, htmlUpdates, oldBookPath, newBookPath);
                    if (!string.Equals(updated, srcset, StringComparison.Ordinal))
                    {
                        element.SetAttribute("srcset", updated);
                    }
                }
            }
        }

        if (cssUpdates.Count > 0)
        {
            foreach (IElement element in document.All)
            {
                string? style = element.GetAttribute("style");
                if (!string.IsNullOrEmpty(style))
                {
                    string updated = PerformCssUpdates.Apply(style, cssUpdates, oldBookPath, newBookPath);
                    if (!string.Equals(updated, style, StringComparison.Ordinal))
                    {
                        element.SetAttribute("style", updated);
                    }
                }

                if (string.Equals(element.LocalName, "style", StringComparison.OrdinalIgnoreCase))
                {
                    string css = element.TextContent;
                    string updated = PerformCssUpdates.Apply(css, cssUpdates, oldBookPath, newBookPath);
                    if (!string.Equals(updated, css, StringComparison.Ordinal))
                    {
                        element.TextContent = updated;
                    }
                }
            }
        }

        return CleanSource.ReserializeXhtmlDocument(document, version);
    }

    /// <summary>
    /// Updates the path part of each candidate in <c>srcset</c> (<c>"a.jpg 1x, b.jpg 2x"</c>),
    /// preserving the density/width descriptors. The whole value cannot be treated as a single URL,
    /// which would not work correctly with more than one candidate.
    /// </summary>
    private static string UpdateSrcset(
        string srcset,
        IReadOnlyDictionary<string, string> updates,
        string oldBookPath,
        string newBookPath)
    {
        string[] candidates = srcset.Split(',');
        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i].Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            int spaceIndex = candidate.AsSpan().IndexOfAny(SrcsetCandidateSeparators);
            string url = spaceIndex >= 0 ? candidate[..spaceIndex] : candidate;
            string descriptor = spaceIndex >= 0 ? candidate[spaceIndex..] : string.Empty;

            string updatedUrl = HrefUpdate.UpdateValue(url, updates, oldBookPath, newBookPath);
            candidates[i] = updatedUrl + descriptor;
        }

        return string.Join(", ", candidates);
    }
}
