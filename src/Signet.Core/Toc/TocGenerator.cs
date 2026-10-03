using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Semantics;

namespace Signet.Core.Toc;

/// <summary>
/// View-independent orchestration of table of contents generation (without the dialog / tab layer).
/// The heading selection dialog (<c>HeadingSelectorModel</c>) is invoked separately by the App layer before <see cref="GenerateToc"/>.
/// </summary>
public static class TocGenerator
{
    /// <summary>
    /// Rebuilds the table of contents from the book's headings: the <c>nav[epub:type=toc]</c> section (EPUB 3) or
    /// the <c>navMap</c> of the NCX (EPUB 2), after the dialog is closed.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns><c>true</c> if the content of the table of contents changed.</returns>
    public static bool GenerateToc(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (book.GetHtmlResources().Count == 0)
        {
            return false;
        }

        if (book.EpubVersion.StartsWith('3') && book.GetNavResource() is { } nav)
        {
            return new NavProcessor(nav).GenerateTocFromBookContents(book);
        }

        return NcxGenerator.GenerateFromBookContents(book);
    }

    /// <summary>
    /// Creates (or overwrites an existing) XHTML file with the table of contents based on the book's current
    /// table of contents (<see cref="Book.GetTocForDisplay"/>), creates
    /// <c>sgc-toc.css</c> if needed, sets the <c>toc</c> semantics and prepends the new file to the spine.
    /// Without the overwrite dialog and closing the tab —
    /// the App layer decides about that beforehand.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The HTML resource of the table of contents (new or an overwritten existing one).</returns>
    public static HtmlResource CreateHtmlToc(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        string version = book.EpubVersion;
        bool epub3 = version.StartsWith('3');
        HtmlResource? navResource = book.GetNavResource();

        CssResource cssResource =
            book.GetCssResources().FirstOrDefault(
                css => string.Equals(css.Filename, TocHtmlWriter.SgcTocCssFilename, StringComparison.OrdinalIgnoreCase))
            ?? book.CreateHtmlTocCssFile();

        List<HtmlResource> htmlResources = book.GetHtmlResources().ToList();
        if (epub3 && navResource is not null && !book.IsNavInSpine)
        {
            htmlResources.Remove(navResource);
        }

        NavProcessor? navProcessor = epub3 && navResource is not null ? new NavProcessor(navResource) : null;

        HtmlResource? tocResource = null;
        foreach (HtmlResource html in htmlResources)
        {
            if (ReferenceEquals(html, navResource))
            {
                continue;
            }

            string semanticCode = navProcessor is not null
                ? navProcessor.GetLandmarkCodeForResource(html)
                : book.GetOpf().GetGuideSemanticCodeForResource(html);

            if (string.Equals(semanticCode, "toc", StringComparison.Ordinal))
            {
                tocResource = html;
                break;
            }

            if (tocResource is null &&
                string.Equals(html.Filename, TocHtmlWriter.HtmlTocFilename, StringComparison.OrdinalIgnoreCase))
            {
                tocResource = html;
            }
        }

        if (tocResource is null)
        {
            tocResource = book.CreateEmptyHtmlFile();
            tocResource.RenameTo(TocHtmlWriter.HtmlTocFilename);
            htmlResources.Remove(tocResource);
            htmlResources.Insert(0, tocResource);
            book.GetOpf().UpdateSpineOrder(htmlResources);
        }

        string title = Landmarks.GetTitle("toc", book.GetOpf().GetPrimaryBookLanguage());

        IReadOnlyList<TocHtmlWriter.Entry> entries =
            book.GetTocForDisplay().Select(ToWriterEntry).ToList();

        tocResource.SetText(TocHtmlWriter.WriteXml(tocResource.BookPath, cssResource.BookPath, entries, title, version));

        if (navProcessor is not null)
        {
            navProcessor.AddLandmarkCode(tocResource, "toc", toggle: false);
        }
        else
        {
            book.GetOpf().AddGuideSemanticCode(tocResource, "toc", toggle: false);
        }

        book.Modified = true;
        return tocResource;
    }

    private static TocHtmlWriter.Entry ToWriterEntry(TocDisplayEntry entry) => new()
    {
        Text = entry.Title,
        TargetBookPath = entry.TargetBookPath,
        Fragment = entry.Fragment,
        Children = entry.Children.Select(ToWriterEntry).ToList(),
    };
}
