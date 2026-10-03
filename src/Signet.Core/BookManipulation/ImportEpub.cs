using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.Core.Importers;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Localization;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The full pipeline for importing an <c>.epub</c> file into the <see cref="Book"/> model.
/// </summary>
/// <remarks>
/// <para>Steps: unpack the OCF (<see cref="OcfReader"/>) &#8594; read the OPF &#8594; create the
/// resources per manifest entry &#8594; locate / create the NCX &#8594; locate the Nav
/// (EPUB 3) &#8594; de-obfuscate the fonts declared in <c>encryption.xml</c> &#8594; collect the
/// load warnings.</para>
/// <para>The XHTML/XML "well-formed" check (<see cref="WellFormedChecker"/>)
/// does not expand DTDs or named HTML entities and never runs auto-repair (Mend); "Clean on
/// Open" is handled elsewhere. Files are loaded sequentially.</para>
/// </remarks>
public sealed class ImportEpub : IImporter
{
    private const string NcxMimetype = "application/x-dtbncx+xml";
    private const string NcxExtension = "ncx";
    private const string AppleDisplayOptions = "META-INF/com.apple.ibooks.display-options.xml";

    private static readonly string[] FontExtensions = { "ttf", "otf", "ttc", "otc", "woff", "woff2", "dfont" };

    private readonly string _epubPath;

    /// <summary>Creates an importer for the given <c>.epub</c> file.</summary>
    /// <param name="epubPath">The path of the EPUB archive.</param>
    public ImportEpub(string epubPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);
        _epubPath = epubPath;
    }

    /// <inheritdoc />
    public string FullFilePath => _epubPath;

    /// <inheritdoc />
    /// <remarks>For EPUB the validation happens during <see cref="GetBook"/>; here it is always <c>null</c>.</remarks>
    public WellFormedResult? CheckValidToLoad() => null;

    /// <summary>
    /// Loads and parses the EPUB, building a complete <see cref="Book"/> model.
    /// </summary>
    /// <param name="extractMetadata">
    /// When <c>true</c> and the OPF has no valid spine — the original DC metadata is restored after the
    /// OPF is rebuilt.
    /// </param>
    /// <exception cref="EpubLoadException">The EPUB is unreadable, DRM-encrypted or corrupted.</exception>
    public Book GetBook(bool extractMetadata = true)
    {
        if (!File.Exists(_epubPath))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_CannotReadEpub", _epubPath));
        }

        TempFolder tempFolder = new();
        FolderKeeper folderKeeper = new(tempFolder);
        try
        {
            OcfContainer container = new OcfReader(_epubPath).Extract(folderKeeper.MainFolderPath);
            return BuildBook(folderKeeper, container, extractMetadata, fontsAreObfuscatedOnDisk: true);
        }
        catch
        {
            folderKeeper.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads a book from an <b>already unpacked</b> working folder — a checkpoint state
    /// folder (<see cref="CheckpointHistory"/>). The returned <see cref="Book"/> works
    /// directly in <paramref name="folder"/>, but does not own it: its
    /// <see cref="Book.Dispose"/> does not delete the folder.
    /// </summary>
    /// <remarks>
    /// Fonts in the working folder are already decrypted, so they are not processed again; their
    /// obfuscation algorithms come from <see cref="BookStateFile"/> (not from the stale
    /// <c>META-INF/encryption.xml</c>). Load warnings are collected as during an import.
    /// </remarks>
    /// <param name="folder">The working folder with the book's files.</param>
    /// <exception cref="EpubLoadException">The folder does not contain a valid publication.</exception>
    public static Book LoadWorkingFolder(TempFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        FolderKeeper folderKeeper = new(folder, ownsTempFolder: false);
        try
        {
            OcfContainer container = OcfReader.ReadExtracted(folder.Path) with
            {
                EncryptedFiles = BookStateFile.ReadFontObfuscation(folder.Path),
            };
            return BuildBook(folderKeeper, container, extractMetadata: true, fontsAreObfuscatedOnDisk: false);
        }
        catch
        {
            folderKeeper.Dispose();
            throw;
        }
    }

    private static Book BuildBook(
        FolderKeeper folderKeeper,
        OcfContainer container,
        bool extractMetadata,
        bool fontsAreObfuscatedOnDisk)
    {
        RejectDrm(container);

        Book book = new(folderKeeper);
        foreach (string warning in container.Warnings)
        {
            book.AddLoadWarning(warning);
        }

        string opfBookPath = container.OpfBookPath;
        string opfDir = CoreBookPath.StartingDir(opfBookPath);
        string opfText = Utility.ReadUnicodeTextFile(container.ToAbsolutePath(opfBookPath));

        if (!IsWellFormedXml(opfText, out string opfError))
        {
            book.AddLoadWarning(CoreStrings.Format("LoadWarning_OpfMalformed", opfError));
        }

        OpfDocument opfDoc = OpfDocument.Parse(opfText);

        string version = opfDoc.Package.Version;
        if (version is "1.0" or "")
        {
            version = "2.0";
        }

        book.EpubVersion = version;

        (string uniqueIdentifierValue, string uuidIdentifierValue) = ReadIdentifiers(opfDoc);

        // The OPF resource — first the default content (AddOpfToFolder), then the real one.
        OpfResource opf = folderKeeper.AddOpfToFolder(version, opfBookPath);
        opf.EpubVersion = version;
        opf.SetText(opfText);
        opf.CurrentBookRelPath = opfBookPath;
        opf.SaveToDisk();

        ManifestScan scan = ScanManifest(book, opfDoc, opfDir);

        AddUnmanifestedExtras(scan, opfDoc, container, opfDir);

        folderKeeper.SetGroupFolders(scan.ManifestFilePaths, scan.ManifestMediaTypes);

        LoadContentFiles(book, folderKeeper, container, scan);

        // NCX — locate it or (EPUB 2) create it.
        string spineTocId = opfDoc.SpineAttributes.Attributes.Value("toc");
        NcxLocation ncx = LocateOrCreateNcx(
            book, folderKeeper, container, scan, opfDir, version, spineTocId, uuidIdentifierValue);

        LoadHtmlAndCheckWellFormed(book, folderKeeper);

        DeobfuscateFonts(
            book, folderKeeper, container, uniqueIdentifierValue, uuidIdentifierValue, fontsAreObfuscatedOnDisk);

        // EPUB 3 — make sure a navigation document exists.
        if (version.StartsWith('3'))
        {
            HtmlResource? navResource = scan.NavBookPath.Length > 0
                ? folderKeeper.GetResourceByBookPathNoThrow(scan.NavBookPath) as HtmlResource
                : null;
            if (navResource is null)
            {
                book.CreateEmptyNavFile(updateOpf: true);
                book.AddLoadWarning(CoreStrings.Get("LoadWarning_NavCreated"));
            }
        }

        // An NCX not declared in the EPUB 2 manifest — add an <item> entry.
        string ncxId = ncx.ManifestId;
        if (ncx.NotInManifest && version.StartsWith('2') && folderKeeper.Ncx is not null)
        {
            ncxId = opf.AddNcxItem(ncx.BookPath);
        }

        if (folderKeeper.Ncx is not null)
        {
            if (!string.IsNullOrEmpty(ncxId))
            {
                opf.UpdateNcxOnSpine(ncxId);
            }

            opf.UpdateNcxLocationInManifest(folderKeeper.Ncx);
        }

        // No spine — rebuild the OPF, preserving the metadata.
        if (opfDoc.Spine.Count == 0)
        {
            IReadOnlyList<MetaEntry> originalMetadata = opf.GetDcMetadata();
            opf.AutoFixWellFormedErrors();
            if (extractMetadata)
            {
                opf.SetDcMetadata(originalMetadata);
            }

            book.AddLoadWarning(CoreStrings.Get("LoadWarning_SpineCreated"));
        }

        WarnAboutFilesNotInManifest(book, folderKeeper, opfBookPath);

        if (scan.DuplicateFilePaths.Count > 0)
        {
            book.AddLoadWarning(
                CoreStrings.Format("LoadWarning_DuplicateManifestPaths", string.Join(", ", scan.DuplicateFilePaths)));
        }

        folderKeeper.UpdateShortPathNames();
        folderKeeper.PerformInitialLoads();

        book.Modified = book.LoadWarnings.Count > 0;
        return book;
    }

    // -----------------------------------------------------------------
    //  Stages
    // -----------------------------------------------------------------

    private static void RejectDrm(OcfContainer container)
    {
        foreach (string algorithm in container.EncryptedFiles.Values)
        {
            if (algorithm != OcfReader.IdpfFontAlgorithmId && algorithm != OcfReader.AdobeFontAlgorithmId)
            {
                throw new EpubLoadException(
                    CoreStrings.Get("LoadError_Drm"),
                    container.Warnings.ToList());
            }
        }
    }

    private static (string UniqueId, string UuidId) ReadIdentifiers(OpfDocument opfDoc)
    {
        string uniqueIdentifierId = opfDoc.Package.UniqueIdentifier;
        string uniqueIdentifierValue = string.Empty;
        string uuidIdentifierValue = string.Empty;

        foreach (MetaEntry entry in opfDoc.Metadata.Where(m => string.Equals(m.Name, "dc:identifier", StringComparison.Ordinal)))
        {
            string id = entry.Attributes.Value("id");
            string scheme = entry.Attributes.Value("opf:scheme");
            if (scheme.Length == 0)
            {
                scheme = entry.Attributes.Value("scheme");
            }

            string value = Utility.DecodeXml(entry.Content).Trim();

            if (id.Length > 0 && string.Equals(id, uniqueIdentifierId, StringComparison.Ordinal))
            {
                uniqueIdentifierValue = value;
            }

            if (uuidIdentifierValue.Length == 0
                && (value.Contains("urn:uuid:", StringComparison.Ordinal)
                    || string.Equals(scheme, "uuid", StringComparison.OrdinalIgnoreCase)))
            {
                uuidIdentifierValue = value;
            }
        }

        return (uniqueIdentifierValue, uuidIdentifierValue);
    }

    private sealed class ManifestScan
    {
        public List<string> ManifestFilePaths { get; } = new();

        public List<string> ManifestMediaTypes { get; } = new();

        public List<(string BookPath, string MediaType)> ContentToLoad { get; } = new();

        public Dictionary<string, string> NcxCandidates { get; } = new(StringComparer.Ordinal);

        public List<string> DuplicateFilePaths { get; } = new();

        public HashSet<string> SeenPaths { get; } = new(StringComparer.Ordinal);

        public string NavBookPath { get; set; } = string.Empty;
    }

    private static ManifestScan ScanManifest(Book book, OpfDocument opfDoc, string opfDir)
    {
        ManifestScan scan = new();

        foreach (ManifestEntry item in opfDoc.Manifest)
        {
            string href = item.Href;
            if (href.Length == 0 || href.Contains(':', StringComparison.Ordinal))
            {
                // href pointing outside the epub (EPUB 3) — such a file is not managed.
                continue;
            }

            string decoded = Utility.UrlDecodePath(href);
            string itemBookPath = CoreBookPath.BuildBookPath(decoded, opfDir);
            string extension = Path.GetExtension(decoded).TrimStart('.').ToLowerInvariant();
            string type = item.MediaType;

            if (type.Length == 0 || MediaTypes.GetGroupFromMediaType(type).Length == 0)
            {
                string sniffed = MediaTypes.GetMediaTypeFromExtension(extension);
                book.AddLoadWarning(
                    CoreStrings.Format("LoadWarning_UnknownMediaType", decoded, type, sniffed));
                if (sniffed.Length > 0)
                {
                    type = sniffed;
                }
            }

            if (itemBookPath.StartsWith("META-INF/", StringComparison.Ordinal) || itemBookPath == "mimetype")
            {
                book.AddLoadWarning(
                    CoreStrings.Format("LoadWarning_MetaInfInManifest", decoded));
                continue;
            }

            if (string.Equals(type, NcxMimetype, StringComparison.Ordinal) || extension == NcxExtension)
            {
                scan.NcxCandidates[item.Id] = itemBookPath;
                scan.ManifestFilePaths.Add(itemBookPath);
                scan.ManifestMediaTypes.Add(type);
                continue;
            }

            if (!scan.SeenPaths.Add(itemBookPath))
            {
                if (!scan.DuplicateFilePaths.Contains(itemBookPath))
                {
                    scan.DuplicateFilePaths.Add(itemBookPath);
                }

                continue;
            }

            scan.ManifestFilePaths.Add(itemBookPath);
            scan.ManifestMediaTypes.Add(type);
            scan.ContentToLoad.Add((itemBookPath, type));

            string properties = item.Attributes.Value("properties");
            if (properties.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("nav"))
            {
                scan.NavBookPath = itemBookPath;
            }
        }

        return scan;
    }

    private static void AddUnmanifestedExtras(ManifestScan scan, OpfDocument opfDoc, OcfContainer container, string opfDir)
    {
        // EPUB 3: local metadata files referenced by <link href> (they are not, and cannot be, in the manifest).
        foreach (MetaEntry link in opfDoc.Metadata.Where(m => string.Equals(m.Name, "link", StringComparison.Ordinal)))
        {
            string href = link.Attributes.Value("href");
            if (href.Length == 0 || href.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            string linkBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(href), opfDir);
            if (!File.Exists(container.ToAbsolutePath(linkBookPath)) || !scan.SeenPaths.Add(linkBookPath))
            {
                continue;
            }

            string mediaType = link.Attributes.Value("media-type");
            if (mediaType.Length == 0)
            {
                mediaType = MediaTypes.GetMediaTypeFromExtension(
                    Path.GetExtension(linkBookPath).TrimStart('.').ToLowerInvariant());
            }

            scan.ContentToLoad.Add((linkBookPath, mediaType));
        }

        // Obfuscated fonts declared only in encryption.xml (a workaround for old InDesign).
        foreach (KeyValuePair<string, string> encrypted in container.EncryptedFiles)
        {
            string extension = Path.GetExtension(encrypted.Key).TrimStart('.').ToLowerInvariant();
            if (!FontExtensions.Contains(extension) || scan.SeenPaths.Contains(encrypted.Key))
            {
                continue;
            }

            if (File.Exists(container.ToAbsolutePath(encrypted.Key)) && scan.SeenPaths.Add(encrypted.Key))
            {
                scan.ContentToLoad.Add((encrypted.Key, MediaTypes.GetMediaTypeFromExtension(extension)));
            }
        }

        // A non-standard Apple iBooks file.
        if (File.Exists(container.ToAbsolutePath(AppleDisplayOptions)) && scan.SeenPaths.Add(AppleDisplayOptions))
        {
            scan.ContentToLoad.Add((AppleDisplayOptions, "application/vnd.apple.ibooks+xml"));
        }
    }

    private static void LoadContentFiles(Book book, FolderKeeper folderKeeper, OcfContainer container, ManifestScan scan)
    {
        List<string> missing = new();
        foreach ((string bookPath, string mediaType) in scan.ContentToLoad)
        {
            string fullPath = container.ToAbsolutePath(bookPath);
            if (!File.Exists(fullPath))
            {
                missing.Add(bookPath);
                continue;
            }

            folderKeeper.AddContentFileToFolder(fullPath, updateOpf: false, mimeType: mediaType, bookPath: bookPath);
        }

        if (missing.Count > 0)
        {
            book.AddLoadWarning(
                CoreStrings.Format("LoadWarning_ManifestFilesMissing", string.Join(", ", missing)));
        }
    }

    private readonly record struct NcxLocation(string BookPath, string ManifestId, bool NotInManifest);

    private static NcxLocation LocateOrCreateNcx(
        Book book,
        FolderKeeper folderKeeper,
        OcfContainer container,
        ManifestScan scan,
        string opfDir,
        string version,
        string spineTocId,
        string uuidIdentifierValue)
    {
        // 1) the normal case — the NCX id on the spine points at an existing manifest entry.
        if (spineTocId.Length > 0 && scan.NcxCandidates.TryGetValue(spineTocId, out string? tocHrefPath))
        {
            if (File.Exists(container.ToAbsolutePath(tocHrefPath)))
            {
                LoadExistingNcx(book, folderKeeper, container, version, tocHrefPath);
                return new NcxLocation(tocHrefPath, spineTocId, NotInManifest: false);
            }
        }

        // 2) no id on the spine — look in the manifest for a file with the .ncx extension.
        if (spineTocId.Length == 0)
        {
            foreach (KeyValuePair<string, string> candidate in scan.NcxCandidates)
            {
                if (Path.GetExtension(candidate.Value).TrimStart('.').Equals(NcxExtension, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(container.ToAbsolutePath(candidate.Value)))
                {
                    LoadExistingNcx(book, folderKeeper, container, version, candidate.Value);
                    book.AddLoadWarning(
                        CoreStrings.Format("LoadWarning_NcxMisidentified", candidate.Value));
                    return new NcxLocation(candidate.Value, candidate.Key, NotInManifest: false);
                }
            }
        }

        // 3) the NCX is required only in EPUB 2.
        if (version.StartsWith('3'))
        {
            return new NcxLocation(string.Empty, string.Empty, NotInManifest: false);
        }

        // 4) EPUB 2 without an NCX — create a new one.
        string ncxBookPath = opfDir.Length == 0 ? "toc.ncx" : opfDir + "/toc.ncx";
        string firstTextDir = folderKeeper.GetDefaultFolderForGroup("Text");
        NcxResource created = folderKeeper.AddNcxToFolder(version, ncxBookPath, firstTextDir);
        if (uuidIdentifierValue.Length > 0)
        {
            created.SetMainId(uuidIdentifierValue);
            created.SaveToDisk();
        }

        book.AddLoadWarning(CoreStrings.Get("LoadWarning_NcxCreated"));
        return new NcxLocation(ncxBookPath, string.Empty, NotInManifest: true);
    }

    private static void LoadExistingNcx(
        Book book, FolderKeeper folderKeeper, OcfContainer container, string version, string ncxBookPath)
    {
        // The content has to be read BEFORE AddNcxToFolder overwrites the file on disk with the default template
        // (working folder = the unpacked OCF folder). Same as for the OPF in BuildBook.
        string ncxText = Utility.ReadUnicodeTextFile(container.ToAbsolutePath(ncxBookPath));
        if (!IsWellFormedXml(ncxText, out string ncxError))
        {
            book.AddLoadWarning(CoreStrings.Format("LoadWarning_NcxMalformed", ncxError));
        }

        NcxResource ncx = folderKeeper.AddNcxToFolder(version, ncxBookPath);
        ncx.EpubVersion = version;
        ncx.SetText(ncxText);
        ncx.CurrentBookRelPath = ncxBookPath;
        ncx.SaveToDisk();
    }

    private static void LoadHtmlAndCheckWellFormed(Book book, FolderKeeper folderKeeper)
    {
        List<string> problems = new();
        foreach (HtmlResource html in folderKeeper.GetResourceTypeList<HtmlResource>())
        {
            try
            {
                html.SetText(HtmlEncodingResolver.ReadHtmlFile(html.FullPath));
            }
            catch (IOException)
            {
                problems.Add(html.BookPath);
                continue;
            }

            if (!IsWellFormedXml(html.GetText(), out _))
            {
                problems.Add(html.BookPath);
            }
        }

        if (problems.Count > 0)
        {
            book.AddLoadWarning(
                CoreStrings.Format("LoadWarning_HtmlNotWellFormed", string.Join(", ", problems)));
        }
    }

    private static void DeobfuscateFonts(
        Book book,
        FolderKeeper folderKeeper,
        OcfContainer container,
        string uniqueIdentifierValue,
        string uuidIdentifierValue,
        bool fontsAreObfuscatedOnDisk)
    {
        if (container.EncryptedFiles.Count == 0)
        {
            return;
        }

        foreach (FontResource font in folderKeeper.GetResourceTypeList<FontResource>())
        {
            if (!container.EncryptedFiles.TryGetValue(font.BookPath, out string? algorithm)
                || string.IsNullOrEmpty(algorithm))
            {
                continue;
            }

            font.ObfuscationAlgorithm = algorithm;

            if (!fontsAreObfuscatedOnDisk)
            {
                continue;
            }

            string identifier = string.Equals(algorithm, OcfReader.AdobeFontAlgorithmId, StringComparison.Ordinal)
                ? uuidIdentifierValue
                : uniqueIdentifierValue;

            if (identifier.Length == 0)
            {
                book.AddLoadWarning(
                    CoreStrings.Format("LoadWarning_FontNoIdentifier", font.BookPath));
                continue;
            }

            try
            {
                FontObfuscation.ObfuscateFile(font.FullPath, algorithm, identifier);
            }
            catch (FontObfuscationException ex)
            {
                book.AddLoadWarning(CoreStrings.Format("LoadWarning_FontDeobfuscationFailed", font.BookPath, ex.Message));
            }
        }
    }

    private static void WarnAboutFilesNotInManifest(Book book, FolderKeeper folderKeeper, string opfBookPath)
    {
        List<string> notInManifest = new();
        foreach (string file in Directory.EnumerateFiles(folderKeeper.MainFolderPath, "*", SearchOption.AllDirectories))
        {
            string bookPath = Path.GetRelativePath(folderKeeper.MainFolderPath, file).Replace(Path.DirectorySeparatorChar, '/');
            if (bookPath == "mimetype"
                || bookPath == TempFolder.LockFileName
                || bookPath == BookStateFile.FileName
                || bookPath.StartsWith("META-INF/", StringComparison.Ordinal)
                || string.Equals(bookPath, opfBookPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (folderKeeper.GetResourceByBookPathNoThrow(bookPath) is null)
            {
                notInManifest.Add(bookPath);
            }
        }

        if (notInManifest.Count > 0)
        {
            notInManifest.Sort(StringComparer.Ordinal);
            book.AddLoadWarning(
                CoreStrings.Format("LoadWarning_FilesNotInManifest", string.Join(", ", notInManifest)));
        }
    }

    /// <summary>
    /// The "well-formed" (syntactic validity) check of XML/XHTML text via
    /// <see cref="WellFormedChecker"/>. No DTD or named HTML entity expansion; no auto-repair.
    /// </summary>
    private static bool IsWellFormedXml(string text, out string error)
    {
        WellFormedResult result = WellFormedChecker.Check(text);
        error = result.IsWellFormed ? string.Empty : result.Message;
        return result.IsWellFormed;
    }
}
