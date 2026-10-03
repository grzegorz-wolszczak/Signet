using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Signet.Core.Resources;

/// <summary>
/// A (X)HTML resource of the book. Besides the text it keeps a lazy cache of the DOM tree
/// (AngleSharp, an HTML5 parser) and exposes queries about linked resources and manifest properties.
/// </summary>
/// <remarks>
/// The DOM cache is invalidated on every content change.
/// </remarks>
public class HtmlResource : XmlResource
{
    private static readonly HtmlParser Parser = new();

    private static readonly string[] LinkedResourceTags = { "img", "link", "audio", "video", "source" };

    private static readonly string[] OrderedManifestProperties =
        { "mathml", "svg", "scripted", "switch", "remote-resources" };

    private static readonly Dictionary<string, string> ManifestPropertyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["math"] = "mathml",
        ["svg"] = "svg",
        ["script"] = "scripted",
        ["epub:switch"] = "switch",
    };

    private IHtmlDocument? _dom;

    /// <inheritdoc cref="Resource(string, string)"/>
    public HtmlResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Html;

    /// <inheritdoc/>
    protected override void OnTextChanged() => _dom = null;

    /// <summary>
    /// Returns the (lazily built) DOM document of the current content. Subsequent calls without a text change
    /// return the same object.
    /// </summary>
    public IHtmlDocument GetDocument()
    {
        Lock.EnterUpgradeableReadLock();
        try
        {
            if (_dom is not null)
            {
                return _dom;
            }

            Lock.EnterWriteLock();
            try
            {
                _dom ??= Parser.ParseDocument(GetText());
                return _dom;
            }
            finally
            {
                Lock.ExitWriteLock();
            }
        }
        finally
        {
            Lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>The bookpaths of linked stylesheets (<c>&lt;link rel="stylesheet"&gt;</c>), relative only.</summary>
    public IReadOnlyList<string> GetLinkedStylesheets()
    {
        List<string> result = new();
        foreach (IElement link in GetDocument().QuerySelectorAll("link"))
        {
            string rel = link.GetAttribute("rel") ?? string.Empty;
            if (!rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddRelativeHrefAsBookPath(result, link.GetAttribute("href"));
        }

        return result;
    }

    /// <summary>The bookpaths of linked scripts (<c>&lt;script src&gt;</c>), relative only.</summary>
    public IReadOnlyList<string> GetLinkedJavascripts()
    {
        List<string> result = new();
        foreach (IElement script in GetDocument().QuerySelectorAll("script[src]"))
        {
            AddRelativeHrefAsBookPath(result, script.GetAttribute("src"));
        }

        return result;
    }

    /// <summary>
    /// The bookpaths of all linked resources: images, stylesheets, audio/video/source.
    /// Relative references only.
    /// </summary>
    public IReadOnlyList<string> GetPathsToLinkedResources()
    {
        List<string> result = new();
        foreach (IElement node in GetDocument().All)
        {
            if (!LinkedResourceTags.Contains(node.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (node.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
            {
                string rel = node.GetAttribute("rel") ?? string.Empty;
                if (!rel.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            AddRelativeHrefAsBookPath(result, node.GetAttribute("href"));
            AddRelativeHrefAsBookPath(result, node.GetAttribute("src"));
        }

        return result;
    }

    /// <summary>
    /// The manifest properties derived from the content:
    /// <c>mathml</c>, <c>svg</c>, <c>scripted</c>, <c>switch</c>, <c>remote-resources</c>.
    /// <c>nav</c> is deliberately omitted (it applies only to the navigation document).
    /// </summary>
    public IReadOnlyList<string> GetManifestProperties()
    {
        HashSet<string> properties = new(StringComparer.Ordinal);
        foreach (IElement element in GetDocument().All)
        {
            if (ManifestPropertyMap.TryGetValue(element.LocalName, out string? mapped) ||
                ManifestPropertyMap.TryGetValue(element.TagName, out mapped))
            {
                properties.Add(mapped);
            }

            string? src = element.GetAttribute("src");
            if (!string.IsNullOrEmpty(src) && IsAbsoluteReference(src))
            {
                properties.Add("remote-resources");
            }
        }

        // a stable order
        return OrderedManifestProperties.Where(properties.Contains).ToList();
    }

    /// <summary>
    /// The language code from the <c>&lt;html&gt;</c> element (<c>xml:lang</c> first, then <c>lang</c>);
    /// <c>""</c> if absent.
    /// </summary>
    public string GetLanguageAttribute()
    {
        IElement? html = GetDocument().QuerySelector("html");
        if (html is null)
        {
            return string.Empty;
        }

        return html.GetAttribute("xml:lang") ?? html.GetAttribute("lang") ?? string.Empty;
    }

    private void AddRelativeHrefAsBookPath(List<string> target, string? href)
    {
        if (string.IsNullOrEmpty(href) || IsAbsoluteReference(href))
        {
            return;
        }

        string path = href;
        int hash = path.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            path = path[..hash];
        }

        int query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        if (path.Length == 0)
        {
            return;
        }

        string bookPath = Core.BookPath.BuildBookPath(path, Folder);
        if (!target.Contains(bookPath))
        {
            target.Add(bookPath);
        }
    }

    private static bool IsAbsoluteReference(string reference) =>
        reference.Contains(':', StringComparison.Ordinal);
}
