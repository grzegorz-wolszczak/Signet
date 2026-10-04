using System;
using System.Collections.Generic;
using System.IO;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Localization;
using CoreBookPath = Signet.Core.BookPath;
using SysPath = System.IO.Path;

namespace Signet.Core.Importers;

/// <summary>
/// Imports a single (X)HTML file as a single-section EPUB publication. Pulls in linked, locally
/// existing stylesheets and images (rewriting <c>href</c>/<c>src</c>), skips JS and links to other
/// (X)HTML files, and maps only <c>&lt;title&gt;</c> from the HTML metadata to the OPF.
/// </summary>
public sealed class ImportHtml : IImporter
{
    private readonly string _fullFilePath;
    private readonly string _epubVersion;
    private readonly bool _mendOnOpen;
    private readonly bool _mendAddMissingDoctype;
    private string? _cachedSource;

    /// <summary>Creates an HTML file importer.</summary>
    /// <param name="fullFilePath">Path of the <c>.xhtml</c> / <c>.html</c> / <c>.htm</c> file.</param>
    /// <param name="options">Import settings (EPUB version, repair on open).</param>
    public ImportHtml(string fullFilePath, ImporterOptions options = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFilePath);
        _fullFilePath = fullFilePath;
        string version = options.DefaultVersion;
        _epubVersion = string.IsNullOrWhiteSpace(version) ? "2.0" : version;
        _mendOnOpen = options.MendOnOpen;
        _mendAddMissingDoctype = options.MendAddMissingDoctype;
    }

    /// <inheritdoc />
    public string FullFilePath => _fullFilePath;

    /// <inheritdoc />
    public WellFormedResult? CheckValidToLoad()
    {
        WellFormedResult result = WellFormedChecker.Check(LoadSource(), "application/xhtml+xml");
        return result.IsWellFormed ? null : result;
    }

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

            HtmlResource htmlResource = CreateHtmlResource(folderKeeper);
            string updated = LoadReferencedFilesAndRewrite(folderKeeper, htmlResource, source);
            htmlResource.SetText(updated);
            htmlResource.SaveToDisk();

            if (extractMetadata)
            {
                string title = ExtractTitle(source);
                if (title.Length > 0)
                {
                    book.GetOpf().SetPrimaryBookTitle(title);
                }
            }

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
        if (_cachedSource is not null)
        {
            return _cachedSource;
        }

        string source = HtmlEncodingResolver.ReadHtmlFile(_fullFilePath);
        source = CleanSource.CharToEntity(source, _epubVersion);
        if (_mendOnOpen)
        {
            source = CleanSource.Mend(source, _epubVersion, addMissingDoctype: _mendAddMissingDoctype);
        }

        _cachedSource = source;
        return source;
    }

    private HtmlResource CreateHtmlResource(FolderKeeper folderKeeper)
    {
        string dir = SysPath.Combine(SysPath.GetTempPath(), "signet-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string filePath = SysPath.Combine(dir, SysPath.GetFileNameWithoutExtension(_fullFilePath) + ".xhtml");
        try
        {
            Utility.WriteUnicodeTextFile("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n", filePath);
            Resource resource = folderKeeper.AddContentFileToFolder(filePath, updateOpf: true, "application/xhtml+xml");
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

    private string LoadReferencedFilesAndRewrite(FolderKeeper folderKeeper, HtmlResource htmlResource, string source)
    {
        IHtmlDocument doc = XhtmlDoc.Parse(source);
        string sourceDir = SysPath.GetDirectoryName(SysPath.GetFullPath(_fullFilePath)) ?? string.Empty;

        Dictionary<string, string> rewrites = new(StringComparer.Ordinal);

        foreach ((IElement element, string attribute) in EnumerateReferences(doc))
        {
            string rawValue = element.GetAttribute(attribute) ?? string.Empty;
            if (rawValue.Length == 0 || rewrites.ContainsKey(rawValue))
            {
                continue;
            }

            if (IsExternalOrInPage(rawValue))
            {
                continue;
            }

            (string relPath, _) = SplitFragmentAndQuery(rawValue);
            string decoded = Utility.UrlDecodePath(relPath);
            string diskPath;
            try
            {
                diskPath = SysPath.GetFullPath(SysPath.Combine(sourceDir, decoded));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!File.Exists(diskPath))
            {
                continue;
            }

            string extension = SysPath.GetExtension(diskPath).TrimStart('.').ToLowerInvariant();
            bool isStyle = string.Equals(extension, "css", StringComparison.Ordinal);
            bool isMedia = MediaTypes.GetGroupFromMediaType(MediaTypes.GetMediaTypeFromExtension(extension))
                is "Images" or "Audio" or "Video" or "Fonts";
            if (!isStyle && !isMedia)
            {
                continue;
            }

            Resource added = folderKeeper.AddContentFileToFolder(diskPath, updateOpf: true);
            string newHref = Utility.UrlEncodePath(CoreBookPath.Relative(htmlResource.BookPath, added.BookPath));
            rewrites[rawValue] = newHref;
        }

        string result = source;
        foreach ((string oldValue, string newValue) in rewrites)
        {
            result = result
                .Replace("\"" + oldValue + "\"", "\"" + newValue + "\"", StringComparison.Ordinal)
                .Replace("'" + oldValue + "'", "'" + newValue + "'", StringComparison.Ordinal);
        }

        return result;
    }

    private static IEnumerable<(IElement Element, string Attribute)> EnumerateReferences(IHtmlDocument doc)
    {
        foreach (IElement link in doc.QuerySelectorAll("link[href]"))
        {
            yield return (link, "href");
        }

        foreach (IElement img in doc.QuerySelectorAll("img[src]"))
        {
            yield return (img, "src");
        }
    }

    private static bool IsExternalOrInPage(string value) =>
        value.StartsWith('#')
        || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || value.Contains("://", StringComparison.Ordinal)
        || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith('/');

    private static (string Path, string Suffix) SplitFragmentAndQuery(string value)
    {
        int hash = value.IndexOf('#', StringComparison.Ordinal);
        int query = value.IndexOf('?', StringComparison.Ordinal);
        int cut = (hash, query) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => query,
            (_, < 0) => hash,
            _ => Math.Min(hash, query),
        };
        return cut < 0 ? (value, string.Empty) : (value[..cut], value[cut..]);
    }

    private static string ExtractTitle(string source)
    {
        try
        {
            IHtmlDocument doc = XhtmlDoc.Parse(source);
            return doc.Title?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
