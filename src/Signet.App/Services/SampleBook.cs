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

    // What the first chapter shows in the sample of Code Editor: headings, a class, links (one of them broken, so the highlighting
    // of a broken link shows), an image, a style sheet and a line with the characters that are drawn as nothing
    // (so that the three settings of Preferences show what they do), written the way a book is. The paragraphs sit in divs
    // inside a div, so that the line along the block of the caret (a setting) has nested blocks to show.
    private const string FirstChapterTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <head>
          <title>The Lighthouse</title>
          <link rel="stylesheet" type="text/css" href="../Styles/Stylesheet.css"/>
        </head>
        <body>
          <h1 id="start">The Lighthouse</h1>
          <div class="chapter">
            <div class="scene">
              <p class="lead">The sea was <em>calm</em> that morning, and the keeper climbed the stairs.</p>
              <p>Read <a href="Chapter02.xhtml">the next chapter</a> or <a href="Missing.xhtml">a missing page</a>.</p>
            </div>
            <div class="note">
              <p>A <span class="small">short</span> note under the light.</p>
            </div>
          </div>
          <!-- Characters that are drawn as nothing (Code Editor can mark them): a word hyphenated with soft
               hyphens, a non-breaking space and a zero-width space. -->
          <p>Light{SHY}house{SHY}keeper, 5{NBSP}km away{ZWSP}.</p>
          <img src="../Images/Figure01.png" alt="The lighthouse"/>
        </body>
        </html>
        """;

    // The first chapter as the book holds it: the placeholders of the template stand for the characters that are drawn
    // as nothing, which would be invisible (and easy to lose) in the source of this file.
    private static string FirstChapterText => FirstChapterTemplate
        .Replace("{SHY}", "\u00ad", StringComparison.Ordinal)
        .Replace("{NBSP}", "\u00a0", StringComparison.Ordinal)
        .Replace("{ZWSP}", "\u200b", StringComparison.Ordinal);

    // What the second chapter holds for the sample of Cleanup: nested divs with the same attributes, empty elements and a bare span.
    private const string SecondChapterText = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <head>
          <title>The Lamp</title>
          <link rel="stylesheet" type="text/css" href="../Styles/Stylesheet.css"/>
        </head>
        <body>
          <div class="box"><div class="box"><p>The lamp was lit at dusk.</p></div></div>
          <p></p>
          <p>The <span>keeper</span> counted the steps.</p>
          <span class="small"></span>
        </body>
        </html>
        """;

    // A style sheet with rules to merge: one selector twice, and two selectors with the same properties.
    private const string StyleSheetText = """
        p { margin: 0; }
        .note { margin: 1em 0; }
        .box { border: 1px solid #888; }
        .small { font-size: 0.8em; }
        .lead { font-size: 0.8em; }
        p { margin: 0; }
        """;

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

            CssResource stylesheet = book.CreateEmptyCssFile(fileName: "Stylesheet.css");
            stylesheet.SetText(StyleSheetText);
            stylesheet.SaveToDisk();
            book.CreateEmptyCssFile(fileName: "Fonts.css");
            chapters[1].SetText(FirstChapterText);
            chapters[1].SaveToDisk();
            chapters[2].SetText(SecondChapterText);
            chapters[2].SaveToDisk();

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
