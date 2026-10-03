using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Localization;
using SysPath = System.IO.Path;

namespace Signet.Core.Importers;

/// <summary>
/// Imports a plain text file: wraps paragraphs in <c>&lt;p&gt;</c>, repairs (Mend) it into XHTML
/// and creates a single-section EPUB publication from it.
/// </summary>
public sealed class ImportTxt : IImporter
{
    private const string FirstSectionName = "Section0001.xhtml";

    private readonly string _fullFilePath;
    private readonly string _epubVersion;

    /// <summary>Creates a text file importer.</summary>
    /// <param name="fullFilePath">Path of the <c>.txt</c> file.</param>
    /// <param name="options">Import settings (<see cref="ImporterOptions.DefaultVersion"/> is used).</param>
    public ImportTxt(string fullFilePath, ImporterOptions options = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFilePath);
        _fullFilePath = fullFilePath;
        string version = options.DefaultVersion;
        _epubVersion = string.IsNullOrWhiteSpace(version) ? "2.0" : version;
    }

    /// <inheritdoc />
    public string FullFilePath => _fullFilePath;

    /// <inheritdoc />
    public WellFormedResult? CheckValidToLoad() => null;

    /// <inheritdoc />
    public Book GetBook(bool extractMetadata = true)
    {
        if (!File.Exists(_fullFilePath))
        {
            throw new FileNotFoundException(CoreStrings.Get("Error_CannotReadFile"), _fullFilePath);
        }

        TempFolder tempFolder = new();
        FolderKeeper folderKeeper = new(tempFolder);
        try
        {
            Book book = new(folderKeeper) { EpubVersion = _epubVersion };
            folderKeeper.AddOpfToFolder(_epubVersion).EpubVersion = _epubVersion;

            string source = LoadSource();
            HtmlResource htmlResource = CreateHtmlResource(folderKeeper, source);
            htmlResource.SetText(source);
            htmlResource.SaveToDisk();

            ImportSupport.EnsureNavOrNcx(book, folderKeeper, _epubVersion, htmlResource.BookPath);

            folderKeeper.UpdateShortPathNames();
            folderKeeper.PerformInitialLoads();
            book.Modified = true;
            return book;
        }
        catch
        {
            folderKeeper.Dispose();
            throw;
        }
    }

    private string LoadSource()
    {
        string source = Utility.ReadUnicodeTextFile(_fullFilePath);
        source = CreateParagraphs(source.Split('\n'));
        return CleanSource.Mend(source, _epubVersion);
    }

    private static HtmlResource CreateHtmlResource(FolderKeeper folderKeeper, string source)
    {
        string dir = SysPath.Combine(SysPath.GetTempPath(), "signet-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string filePath = SysPath.Combine(dir, FirstSectionName);
        try
        {
            Utility.WriteUnicodeTextFile(source, filePath);
            Resource resource = folderKeeper.AddContentFileToFolder(filePath, updateOpf: true);
            return (HtmlResource)resource;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Folds a list of lines into <c>&lt;p&gt;</c> paragraphs: an empty line or a line starting with
    /// whitespace ends the current paragraph.
    /// </summary>
    private static string CreateParagraphs(IReadOnlyList<string> lines)
    {
        StringBuilder text = new();
        StringBuilder paragraph = new("<p>");

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                paragraph.Append("</p>\n");
                text.Append(paragraph);
                paragraph = new StringBuilder("<p>");
            }

            paragraph.Append(HtmlEscape(" " + line));
        }

        paragraph.Append("</p>\n");
        text.Append(paragraph);
        return text.ToString();
    }

    private static string HtmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
