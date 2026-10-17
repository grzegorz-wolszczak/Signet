using System;
using System.IO;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.App.Services;

/// <summary>
/// A small EPUB 3 book made in memory, for the previews of the Preferences window (the sample Book Browser): a few chapters
/// in the reading order, style sheets, images (one of them the cover), a font and the navigation document, so that every column of
/// the Book Browser has something in it. It never touches a book of the user.
/// </summary>
public static class SampleBook
{
    // A 1 x 1 PNG: the sample images only have to be images the book can read the size of.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly string[] Chapters = { "Titlepage.xhtml", "Chapter01.xhtml", "Chapter02.xhtml", "Chapter03.xhtml" };

    /// <summary>Creates the sample book; the caller disposes it.</summary>
    public static Book Create()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        try
        {
            // The new book has one blank chapter; the others are added after it, in the reading order.
            HtmlResource[] chapters = new HtmlResource[Chapters.Length];
            HtmlResource? nav = book.GetNavResource();
            chapters[0] = book.GetFolderKeeper().GetResourceTypeList<HtmlResource>().FirstOrDefault(h => !ReferenceEquals(h, nav))
                ?? throw new InvalidOperationException("The new book has no blank chapter.");
            for (int i = 1; i < chapters.Length; i++)
            {
                chapters[i] = book.CreateEmptyHtmlFile();
            }

            for (int i = 0; i < chapters.Length; i++)
            {
                chapters[i].RenameTo(Chapters[i]);
            }

            book.CreateEmptyCssFile(fileName: "Stylesheet.css");
            book.CreateEmptyCssFile(fileName: "Fonts.css");

            string folder = Path.Combine(Path.GetTempPath(), "signet-sample-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string cover = Path.Combine(folder, "cover.png");
                string figure = Path.Combine(folder, "Figure01.png");
                string font = Path.Combine(folder, "Serif-Regular.ttf");
                File.WriteAllBytes(cover, Png);
                File.WriteAllBytes(figure, Png);
                File.WriteAllBytes(font, new byte[16]);
                System.Collections.Generic.IReadOnlyList<Resource> added = book.AddExistingFiles(new[] { cover, figure, font });
                book.SetCoverImage((ImageResource)added[0]);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }

            book.Modified = false;
            return book;
        }
        catch
        {
            book.Dispose();
            throw;
        }
    }
}
