using System;
using Signet.Core.BookManipulation;

namespace Signet.Core.Toc;

/// <summary>
/// A view-independent model for the "Edit Table Of Contents" dialog.
/// The source and the target is the nav document (EPUB 3) or the NCX (EPUB 2) — the same base resource choice rule
/// applies to both.
/// </summary>
public static class TocEditModel
{
    /// <summary>
    /// Builds an editable entry tree from the book's current table of contents (nav for EPUB 3, otherwise
    /// the NCX). Returns an empty root if the book has neither a nav nor an NCX.
    /// </summary>
    public static TocEntry GetRootTocEntry(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (book.IsEpub3 && book.GetNavResource() is { } nav)
        {
            return new NavProcessor(nav).GetRootTocEntry();
        }

        if (book.GetNcx() is { } ncx)
        {
            return NcxTocEntries.GetRootTocEntry(ncx);
        }

        return new TocEntry { IsRoot = true };
    }

    /// <summary>
    /// Saves the entry tree: the <c>nav[epub:type=toc]</c> section (EPUB 3) or the <c>navMap</c> of the NCX
    /// (EPUB 2 / no nav). Sets <see cref="Book.Modified"/>.
    /// </summary>
    public static void Save(Book book, TocEntry root)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(root);

        if (book.IsEpub3 && book.GetNavResource() is { } nav)
        {
            new NavProcessor(nav).GenerateNavTocFromTocEntries(root);
        }
        else if (book.GetNcx() is not null)
        {
            NcxGenerator.GenerateFromTocEntries(book, root);
        }
        else
        {
            return;
        }

        book.Modified = true;
    }
}
