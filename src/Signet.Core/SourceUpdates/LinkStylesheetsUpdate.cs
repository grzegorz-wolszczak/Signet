using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Signet.Core.BookManipulation;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Re-links stylesheets in the (X)HTML source. <b>All</b> <c>&lt;link&gt;</c> elements that are
/// direct children of <c>&lt;head&gt;</c> are removed, and new <c>&lt;link rel="stylesheet"&gt;</c>
/// elements are inserted at the end of <c>&lt;head&gt;</c> in the given order.
/// </summary>
public static class LinkStylesheetsUpdate
{
    /// <summary>
    /// Returns the new <paramref name="source"/> with stylesheets linked exactly
    /// in the order of <paramref name="stylesheetBookPaths"/>.
    /// </summary>
    /// <param name="source">The (X)HTML source of the file.</param>
    /// <param name="htmlBookPath">The bookpath of this file (used to compute relative paths).</param>
    /// <param name="stylesheetBookPaths">The bookpaths of the CSS stylesheets to link (order preserved).</param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> / <c>"3.0"</c>) — selects the DOCTYPE on reserialization.</param>
    public static string Apply(
        string source,
        string htmlBookPath,
        IReadOnlyList<string> stylesheetBookPaths,
        string version)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(htmlBookPath);
        ArgumentNullException.ThrowIfNull(stylesheetBookPaths);
        ArgumentNullException.ThrowIfNull(version);

        if (source.Length == 0)
        {
            return source;
        }

        string stripped = CleanSource.StripXmlDeclaration(source);
        HtmlParser parser = new();
        IDocument document = parser.ParseDocument(stripped);

        IElement? head = document.Head;
        if (head is null)
        {
            return source;
        }

        foreach (IElement link in head.Children
                     .Where(e => e.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            link.Remove();
        }

        foreach (string stylesheet in stylesheetBookPaths)
        {
            string href = Utility.UrlEncodePath(Core.BookPath.Relative(htmlBookPath, stylesheet));

            IElement link = document.CreateElement("link");
            link.SetAttribute("href", href);
            link.SetAttribute("type", "text/css");
            link.SetAttribute("rel", "stylesheet");

            head.AppendChild(document.CreateTextNode("\n  "));
            head.AppendChild(link);
        }

        if (stylesheetBookPaths.Count > 0)
        {
            head.AppendChild(document.CreateTextNode("\n"));
        }

        return CleanSource.ReserializeXhtmlDocument(document, version);
    }
}
