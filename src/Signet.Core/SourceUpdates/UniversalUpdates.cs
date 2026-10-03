using System;
using System.Collections.Generic;
using System.IO;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Orchestrates updating references across the whole publication after resources are renamed / moved:
/// splits the change map into HTML/CSS references, walks all text resources and replaces the
/// references to the moved files in them.
/// </summary>
/// <remarks>
/// Covered resource types: <see cref="HtmlResource"/> (through <see cref="PerformHtmlUpdates"/>),
/// <see cref="CssResource"/> (through <see cref="PerformCssUpdates"/>), <see cref="NcxResource"/>
/// (<c>content/@src</c> in <c>navMap</c>/<c>pageList</c>/<c>navList</c>) and <see cref="OpfResource"/>
/// (<c>guide/reference/@href</c> — the manifest is updated separately by
/// <c>OpfResource.ResourceRenamed/Moved/BulkResourcesRenamed/Moved</c>, see <c>FolderKeeper</c>).
/// <para>
/// Deliberately not covered (a pragmatic subset):
/// SMIL / page-map (media overlays), the <c>epub:textref</c> attribute, <c>&lt;link&gt;</c> elements
/// in the OPF metadata (our <see cref="OpfDocument"/> does not model them).
/// </para>
/// </remarks>
public static class UniversalUpdates
{
    private static readonly HashSet<string> FontExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "ttf", "ttc", "otf", "woff", "woff2" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "tif", "tiff", "bm", "bmp", "webp", "avif", "jxl",
    };

    private static readonly HashSet<string> SvgExtensions = new(StringComparer.OrdinalIgnoreCase) { "svg" };
    private static readonly HashSet<string> StyleExtensions = new(StringComparer.OrdinalIgnoreCase) { "css", "xpgt" };

    /// <summary>
    /// Updates references in all resources of the book according to <paramref name="updates"/>.
    /// Runs synchronously.
    /// </summary>
    /// <param name="book">The book whose resources are to be updated.</param>
    /// <param name="updates">A map: old bookpath → new bookpath for every moved resource.</param>
    public static void Perform(Book book, IReadOnlyDictionary<string, string> updates)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return;
        }

        Dictionary<string, string> cssUpdates = SeparateStyleUpdates(updates);

        foreach (Resource resource in book.GetAllResources())
        {
            switch (resource)
            {
                case HtmlResource html:
                    UpdateHtmlResource(html, updates, cssUpdates, book.EpubVersion);
                    break;
                case CssResource css:
                    UpdateCssResource(css, cssUpdates);
                    break;
                case NcxResource ncx:
                    UpdateNcxResource(ncx, updates);
                    break;
                case OpfResource opf:
                    UpdateOpfResource(opf, updates);
                    break;
            }
        }
    }

    /// <summary>Extracts the style-relevant updates (CSS/images/fonts/SVG) from the common map.</summary>
    internal static Dictionary<string, string> SeparateStyleUpdates(IReadOnlyDictionary<string, string> updates)
    {
        Dictionary<string, string> cssUpdates = new(StringComparer.Ordinal);
        foreach ((string oldBookPath, string newBookPath) in updates)
        {
            string extension = Path.GetExtension(oldBookPath).TrimStart('.');
            if (FontExtensions.Contains(extension) || StyleExtensions.Contains(extension)
                || ImageExtensions.Contains(extension) || SvgExtensions.Contains(extension))
            {
                cssUpdates[oldBookPath] = newBookPath;
            }
        }

        return cssUpdates;
    }

    private static void UpdateHtmlResource(
        HtmlResource html,
        IReadOnlyDictionary<string, string> htmlUpdates,
        IReadOnlyDictionary<string, string> cssUpdates,
        string version)
    {
        string oldBookPath = html.CurrentBookRelPath;
        string newBookPath = html.BookPath;
        string source = html.GetText();
        string updated = PerformHtmlUpdates.Apply(source, oldBookPath, newBookPath, htmlUpdates, cssUpdates, html.EpubVersion.Length > 0 ? html.EpubVersion : version);
        if (!string.Equals(updated, source, StringComparison.Ordinal))
        {
            html.SetText(updated);
        }

        html.CurrentBookRelPath = string.Empty;
    }

    private static void UpdateCssResource(CssResource css, Dictionary<string, string> cssUpdates)
    {
        if (cssUpdates.Count == 0)
        {
            css.CurrentBookRelPath = string.Empty;
            return;
        }

        string oldBookPath = css.CurrentBookRelPath;
        string newBookPath = css.BookPath;
        string source = css.GetText();
        string updated = PerformCssUpdates.Apply(source, cssUpdates, oldBookPath, newBookPath);
        if (!string.Equals(updated, source, StringComparison.Ordinal))
        {
            css.SetText(updated);
        }

        css.CurrentBookRelPath = string.Empty;
    }

    private static void UpdateNcxResource(NcxResource ncx, IReadOnlyDictionary<string, string> updates)
    {
        string oldBookPath = ncx.CurrentBookRelPath;
        string newBookPath = ncx.BookPath;
        NcxDocument document = ncx.GetNcxDocument();
        bool changed = false;

        void UpdateSrc(Func<string> get, Action<string> set)
        {
            string value = get();
            if (value.Length == 0)
            {
                return;
            }

            string updated = HrefUpdate.UpdateValue(value, updates, oldBookPath, newBookPath);
            if (!string.Equals(updated, value, StringComparison.Ordinal))
            {
                set(updated);
                changed = true;
            }
        }

        void WalkNavPoint(NcxNavPoint navPoint)
        {
            UpdateSrc(() => navPoint.ContentSrc, v => navPoint.ContentSrc = v);
            foreach (NcxNavPoint child in navPoint.Children)
            {
                WalkNavPoint(child);
            }
        }

        foreach (NcxNavPoint navPoint in document.NavMap)
        {
            WalkNavPoint(navPoint);
        }

        foreach (NcxPageTarget target in document.PageList)
        {
            UpdateSrc(() => target.ContentSrc, v => target.ContentSrc = v);
        }

        foreach (NcxNavList navList in document.NavLists)
        {
            foreach (NcxNavTarget target in navList.Targets)
            {
                UpdateSrc(() => target.ContentSrc, v => target.ContentSrc = v);
            }
        }

        if (changed)
        {
            ncx.SetNcxDocument(document);
        }

        ncx.CurrentBookRelPath = string.Empty;
    }

    private static void UpdateOpfResource(OpfResource opf, IReadOnlyDictionary<string, string> updates)
    {
        string oldBookPath = opf.CurrentBookRelPath;
        string newBookPath = opf.BookPath;
        OpfDocument document = opf.GetOpfDocument();
        bool changed = false;

        foreach (GuideEntry entry in document.Guide)
        {
            if (entry.Href.Length == 0)
            {
                continue;
            }

            string updated = HrefUpdate.UpdateValue(entry.Href, updates, oldBookPath, newBookPath);
            if (!string.Equals(updated, entry.Href, StringComparison.Ordinal))
            {
                entry.Href = updated;
                changed = true;
            }
        }

        if (changed)
        {
            opf.SetOpfDocument(document);
        }

        opf.CurrentBookRelPath = string.Empty;
    }
}
