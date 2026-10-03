using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Signet.Core.BookManipulation;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Re-links scripts in the (X)HTML source. Only those <c>&lt;script&gt;</c> elements that are
/// direct children of <c>&lt;head&gt;</c> and are a "JS link" are removed
/// (a <c>src</c> attribute without <c>:</c> and <c>type</c> = <c>application/javascript</c> or
/// <c>text/javascript</c>). New <c>&lt;script&gt;</c> elements are appended at the end of <c>&lt;head&gt;</c>.
/// </summary>
public static class LinkJavascriptsUpdate
{
    private static readonly HashSet<string> JsLinkTypes = new(StringComparer.Ordinal)
    {
        "application/javascript",
        "text/javascript",
    };

    /// <summary>
    /// Returns the new <paramref name="source"/> with scripts linked exactly
    /// in the order of <paramref name="javascriptBookPaths"/>.
    /// </summary>
    /// <param name="source">The (X)HTML source of the file.</param>
    /// <param name="htmlBookPath">The bookpath of this file (used to compute relative paths).</param>
    /// <param name="javascriptBookPaths">The bookpaths of the scripts to link (order preserved).</param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> / <c>"3.0"</c>) — selects the DOCTYPE on reserialization.</param>
    public static string Apply(
        string source,
        string htmlBookPath,
        IReadOnlyList<string> javascriptBookPaths,
        string version)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(htmlBookPath);
        ArgumentNullException.ThrowIfNull(javascriptBookPaths);
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

        foreach (IElement script in head.Children
                     .Where(IsJsLink)
                     .ToList())
        {
            script.Remove();
        }

        foreach (string javascript in javascriptBookPaths)
        {
            string src = Utility.UrlEncodePath(Core.BookPath.Relative(htmlBookPath, javascript));

            IElement script = document.CreateElement("script");
            script.SetAttribute("type", "text/javascript");
            script.SetAttribute("src", src);

            head.AppendChild(document.CreateTextNode("\n  "));
            head.AppendChild(script);
        }

        if (javascriptBookPaths.Count > 0)
        {
            head.AppendChild(document.CreateTextNode("\n"));
        }

        return CleanSource.ReserializeXhtmlDocument(document, version);
    }

    private static bool IsJsLink(IElement element)
    {
        if (!element.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? src = element.GetAttribute("src");
        string? type = element.GetAttribute("type");
        return !string.IsNullOrEmpty(src)
            && !src.Contains(':', StringComparison.Ordinal)
            && type is not null
            && JsLinkTypes.Contains(type);
    }
}
