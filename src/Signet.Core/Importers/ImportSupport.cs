using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Importers;

/// <summary>Common final steps of the HTML/TXT importers (nav / NCX support).</summary>
internal static class ImportSupport
{
    /// <summary>
    /// After a single text section is created, ensures the publication has a navigation document
    /// (EPUB 3) or an NCX (EPUB 2) pointing to the first XHTML file.
    /// </summary>
    public static void EnsureNavOrNcx(Book book, FolderKeeper folderKeeper, string version, string firstXhtmlBookPath)
    {
        OpfResource opf = book.GetOpf();

        if (version.StartsWith('3'))
        {
            if (opf.GetNavResourceBookPath().Length == 0)
            {
                HtmlResource nav = book.CreateEmptyNavFile(updateOpf: true);
                opf.SetItemRefLinear(nav, false);
            }

            return;
        }

        if (folderKeeper.Ncx is null)
        {
            NcxResource ncx = folderKeeper.AddNcxToFolder(version);
            string ncxId = opf.AddNcxItem(ncx.BookPath);
            opf.UpdateNcxOnSpine(ncxId);
            ncx.FillWithDefaultText(version, firstXhtmlBookPath);
            ncx.SaveToDisk();
        }
    }
}
