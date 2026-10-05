using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Resources;
using Signet.Core.Localization;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The registry of all the book's resources and the operations on that collection.
/// </summary>
/// <remarks>
/// <para>
/// Holds the working folder (<see cref="TempFolder"/>), two maps for O(1) lookup
/// (identifier -&gt; resource, bookpath -&gt; resource) and a group -&gt; folders map that
/// decides which subfolder new files go to.
/// </para>
/// <para>
/// Changes are signalled through .NET events; file watching uses
/// <see cref="FileSystemWatcher"/> (disabled by default —
/// enabled by a constructor flag). OPF synchronization here is
/// <em>minimal</em> — only the manifest (adding / removing / changing the <c>href</c> of an
/// <c>&lt;item&gt;</c> entry); the full OPF logic (spine, properties, guide/landmarks, ID rebasing,
/// bulk operations) lives in <see cref="OpfResource"/>, which also hooks into the
/// <see cref="ResourceAdded"/> / <see cref="ResourceRemoved"/> events.
/// </para>
/// </remarks>
public sealed class FolderKeeper : IDisposable
{
    /// <summary>The file groups honored when filling in missing folders.</summary>
    private static readonly string[] GroupA =
        { "Text", "Styles", "Images", "Fonts", "Audio", "Video", "Misc", "opf", "ncx" };

    /// <summary>The user's file groups without opf/ncx.</summary>
    private static readonly string[] GroupB =
        { "Text", "Styles", "Images", "Fonts", "Audio", "Video", "Misc" };

    private static readonly Regex TrailingDigits = new(@"\d+$", RegexOptions.Compiled);

    private const string ContainerXmlTemplate =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
        "    <rootfiles>\n" +
        "        <rootfile full-path=\"{0}\" media-type=\"application/oebps-package+xml\"/>\n" +
        "   </rootfiles>\n" +
        "</container>\n";

    private readonly object _accessLock = new();
    private readonly Dictionary<string, Resource> _idToResource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Resource> _pathToResource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _groupToFolders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _stdGroupToFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watchedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _suspendedWatchedFiles = new();

    private readonly TempFolder _tempFolder;
    private readonly bool _fileWatchingEnabled;
    private bool _ownsTempFolder = true;
    private FileSystemWatcher? _fsWatcher;
    private OpfResource? _opf;
    private NcxResource? _ncx;
    private bool _disposed;

    /// <summary>
    /// Creates a registry with its own, new working folder.
    /// </summary>
    /// <param name="enableFileWatching">
    /// When <c>true</c>, files registered via <see cref="WatchResourceFile"/> are watched
    /// for external changes (the <see cref="ResourceFileChangedOnDisk"/> event). <c>false</c> by default.
    /// </param>
    public FolderKeeper(bool enableFileWatching = false)
        : this(new TempFolder(), enableFileWatching)
    {
    }

    /// <summary>
    /// Creates a registry using the given working folder. <see cref="FolderKeeper"/>
    /// takes ownership of <paramref name="tempFolder"/> and deletes it in <see cref="Dispose"/>.
    /// </summary>
    /// <param name="tempFolder">The working folder of the unpacked EPUB.</param>
    /// <param name="enableFileWatching">As in <see cref="FolderKeeper(bool)"/>.</param>
    public FolderKeeper(TempFolder tempFolder, bool enableFileWatching = false)
        : this(tempFolder, ownsTempFolder: true, enableFileWatching)
    {
    }

    /// <summary>
    /// Creates a registry using the given working folder, with explicitly specified ownership.
    /// </summary>
    /// <param name="tempFolder">The working folder of the unpacked EPUB.</param>
    /// <param name="ownsTempFolder">
    /// When <c>false</c>, <see cref="Dispose"/> does not delete the folder (its owner is e.g.
    /// <see cref="CheckpointHistory"/>).
    /// </param>
    /// <param name="enableFileWatching">As in <see cref="FolderKeeper(bool)"/>.</param>
    public FolderKeeper(TempFolder tempFolder, bool ownsTempFolder, bool enableFileWatching = false)
    {
        ArgumentNullException.ThrowIfNull(tempFolder);
        _tempFolder = tempFolder;
        _ownsTempFolder = ownsTempFolder;
        _fileWatchingEnabled = enableFileWatching;
        MainFolderPath = _tempFolder.Path;
        CreateGroupToFoldersMap();
    }

    /// <summary>Raised after a resource is added to the registry (when <c>updateOpf</c> = <c>true</c>).</summary>
    public event EventHandler<ResourceEventArgs>? ResourceAdded;

    /// <summary>Raised after a resource is removed from the registry.</summary>
    public event EventHandler<ResourceEventArgs>? ResourceRemoved;

    /// <summary>Raised when a watched file changed on disk outside the application.</summary>
    public event EventHandler<ResourceEventArgs>? ResourceFileChangedOnDisk;

    /// <summary>The full path of the publication's root folder (never ends with a separator).</summary>
    public string MainFolderPath { get; }

    /// <summary>The book's OPF document (<c>null</c> until added).</summary>
    public OpfResource? Opf => _opf;

    /// <summary>The book's NCX resource (<c>null</c> in an EPUB 3 without an NCX or before it is added).</summary>
    public NcxResource? Ncx => _ncx;

    // ---------------------------------------------------------------------
    //  Adding resources
    // ---------------------------------------------------------------------

    /// <summary>
    /// Determines the file's group (<c>Text</c>, <c>Styles</c>, <c>Images</c>, <c>Fonts</c>, <c>Audio</c>,
    /// <c>Video</c>, <c>Misc</c>, <c>other</c>) from the MIME type or — when it is missing —
    /// from the extension.
    /// </summary>
    public string DetermineFileGroup(string filePath, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        mimeType ??= string.Empty;

        if (filePath.Contains("META-INF", StringComparison.Ordinal))
        {
            return "other";
        }

        string extension = SysPath.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        string mt = mimeType;
        if (string.IsNullOrEmpty(mt))
        {
            mt = MediaTypes.GetMediaTypeFromExtension(extension);
            if (string.IsNullOrEmpty(mt))
            {
                return "Misc";
            }
        }

        string group = MediaTypes.GetGroupFromMediaType(mt);
        if (string.IsNullOrEmpty(group))
        {
            mt = MediaTypes.GetMediaTypeFromExtension(extension);
            if (!string.IsNullOrEmpty(mt))
            {
                group = MediaTypes.GetGroupFromMediaType(mt);
            }
        }

        return string.IsNullOrEmpty(group) ? "Misc" : group;
    }

    /// <summary>
    /// Copies a file into the working folder (into the group's proper subfolder or to the given
    /// bookpath) and creates the matching <see cref="Resource"/> object. Does not handle the OPF or NCX — those have
    /// dedicated methods, <see cref="AddOpfToFolder"/> / <see cref="AddNcxToFolder"/>.
    /// </summary>
    /// <param name="fullFilePath">The full path of the source file on disk.</param>
    /// <param name="updateOpf">When <c>true</c>, raises <see cref="ResourceAdded"/> and adds an entry to the OPF manifest.</param>
    /// <param name="mimeType">The MIME type from the manifest (optional — otherwise taken from the extension).</param>
    /// <param name="bookPath">The target bookpath (when given, it forces the file's name and location).</param>
    /// <param name="folderPath">
    /// The target folder (a folder bookpath). The value <c>"\\"</c> means "use the group's default folder".
    /// </param>
    /// <returns>The newly created resource.</returns>
    public Resource AddContentFileToFolder(
        string fullFilePath,
        bool updateOpf = true,
        string? mimeType = null,
        string? bookPath = null,
        string folderPath = "\\")
    {
        ArgumentException.ThrowIfNullOrEmpty(fullFilePath);
        if (!File.Exists(fullFilePath))
        {
            throw new FileNotFoundException(CoreStrings.Get("Error_SourceFileMissing"), fullFilePath);
        }

        string normalizedSourcePath = fullFilePath;
        string filename = SysPath.GetFileName(normalizedSourcePath);

        string mt = mimeType ?? string.Empty;
        if (!string.IsNullOrEmpty(mt) && string.IsNullOrEmpty(MediaTypes.GetGroupFromMediaType(mt)))
        {
            mt = string.Empty;
        }

        if (string.IsNullOrEmpty(mt))
        {
            string extension = SysPath.GetExtension(normalizedSourcePath).TrimStart('.').ToLowerInvariant();
            mt = MediaTypes.GetMediaTypeFromExtension(extension, mimeType ?? string.Empty);
        }

        string group = DetermineFileGroup(normalizedSourcePath, mt);
        bool isMetaInf = fullFilePath.Contains("META-INF", StringComparison.Ordinal);

        Resource resource;
        string newFilePath;
        string resourceBookPath;

        lock (_accessLock)
        {
            if (!string.IsNullOrEmpty(bookPath))
            {
                resourceBookPath = bookPath;
                string startDir = Core.BookPath.StartingDir(bookPath);
                if (!string.IsNullOrEmpty(startDir))
                {
                    Directory.CreateDirectory(ToSystemPath(startDir));
                }

                newFilePath = ToSystemPath(bookPath);
            }
            else if (isMetaInf)
            {
                // Files in META-INF other than container.xml / encryption.xml pass through unchanged.
                int idx = fullFilePath.IndexOf("META-INF", StringComparison.Ordinal);
                resourceBookPath = fullFilePath[idx..].Replace('\\', '/');
                newFilePath = ToSystemPath(resourceBookPath);
                Directory.CreateDirectory(SysPath.GetDirectoryName(newFilePath)!);
            }
            else
            {
                if (filename.StartsWith('.'))
                {
                    filename = filename[1..];
                    normalizedSourcePath = SysPath.Combine(
                        SysPath.GetDirectoryName(normalizedSourcePath) ?? string.Empty, filename);
                }

                filename = GetUniqueFilenameVersion(filename);
                string folderToUse = folderPath == "\\" ? GetDefaultFolderForGroup(group) : folderPath;
                if (!string.IsNullOrEmpty(folderToUse))
                {
                    Directory.CreateDirectory(ToSystemPath(folderToUse));
                    resourceBookPath = folderToUse + "/" + filename;
                }
                else
                {
                    resourceBookPath = filename;
                }

                newFilePath = ToSystemPath(resourceBookPath);
            }

            resource = isMetaInf
                ? new Resource(MainFolderPath, newFilePath)
                : ResourceFactory.Create(MainFolderPath, newFilePath, mt);

            resource.MediaType = mt;
            resource.EpubVersion = _opf?.EpubVersion ?? "2.0";
            resource.ShortPathName = SysPath.GetFileName(newFilePath);

            _idToResource[resource.Identifier] = resource;
            _pathToResource[resourceBookPath] = resource;
        }

        if (!SysPath.GetFullPath(fullFilePath).Equals(SysPath.GetFullPath(newFilePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(fullFilePath, newFilePath, overwrite: true);
        }

        WireResourceEvents(resource);

        if (updateOpf)
        {
            _opf?.AddResource(resource);
            ResourceAdded?.Invoke(this, new ResourceEventArgs(resource));
        }

        return resource;
    }

    /// <summary>
    /// Creates an OPF resource with the default content for the given EPUB version and registers it as
    /// <see cref="Opf"/>. Also updates <c>META-INF/container.xml</c>.
    /// </summary>
    /// <param name="version">The EPUB version (<c>2.0</c> / <c>3.0</c>).</param>
    /// <param name="bookPath">The target OPF bookpath (<c>OEBPS/content.opf</c> by default).</param>
    public OpfResource AddOpfToFolder(string version, string? bookPath = null)
    {
        lock (_accessLock)
        {
            string opfDir = GetDefaultFolderForGroup("opf");
            string opfBookPath = string.IsNullOrEmpty(opfDir) ? "content.opf" : opfDir + "/content.opf";
            if (!string.IsNullOrEmpty(bookPath))
            {
                opfBookPath = bookPath;
            }

            string fullPath = ToSystemPath(opfBookPath);
            Directory.CreateDirectory(SysPath.GetDirectoryName(fullPath)!);

            _opf = new OpfResource(MainFolderPath, fullPath);
            _opf.FillWithDefaultText(version);
            _opf.MediaType = OpfMediaType;
            _opf.ShortPathName = SysPath.GetFileName(fullPath);
            _opf.SaveToDisk();

            _idToResource[_opf.Identifier] = _opf;
            _pathToResource[_opf.BookPath] = _opf;
            WireResourceEvents(_opf);

            UpdateContainerXml(MainFolderPath, opfBookPath);
            return _opf;
        }
    }

    /// <summary>
    /// Creates an NCX resource with the default content and registers it as <see cref="Ncx"/>.
    /// </summary>
    /// <param name="version">The EPUB version.</param>
    /// <param name="bookPath">The target NCX bookpath (<c>OEBPS/toc.ncx</c> by default).</param>
    /// <param name="firstTextDir">
    /// The folder of the first text section (a bookpath). The value <c>"\\"</c> = the default folder of the <c>Text</c> group.
    /// </param>
    public NcxResource AddNcxToFolder(string version, string? bookPath = null, string firstTextDir = "\\")
    {
        lock (_accessLock)
        {
            string ncxDir = GetDefaultFolderForGroup("ncx");
            string ncxBookPath = string.IsNullOrEmpty(ncxDir) ? "toc.ncx" : ncxDir + "/toc.ncx";
            if (!string.IsNullOrEmpty(bookPath))
            {
                ncxBookPath = bookPath;
            }

            string textDir = firstTextDir == "\\" ? GetDefaultFolderForGroup("Text") : firstTextDir;
            string firstSectionBookPath = string.IsNullOrEmpty(textDir)
                ? "Section0001.xhtml"
                : textDir + "/Section0001.xhtml";

            string fullPath = ToSystemPath(ncxBookPath);
            Directory.CreateDirectory(SysPath.GetDirectoryName(fullPath)!);

            _ncx = new NcxResource(MainFolderPath, fullPath)
            {
                EpubVersion = version,
                MediaType = NcxMediaType,
            };
            _ncx.ShortPathName = SysPath.GetFileName(fullPath);
            _ncx.FillWithDefaultText(version, firstSectionBookPath);
            if (_opf is not null)
            {
                _ncx.SetMainId(_opf.GetMainIdentifierValue());
            }

            _ncx.SaveToDisk();
            _idToResource[_ncx.Identifier] = _ncx;
            _pathToResource[_ncx.BookPath] = _ncx;
            WireResourceEvents(_ncx);
            return _ncx;
        }
    }

    /// <summary>Removes the NCX resource from the registry and deletes its file.</summary>
    public void RemoveNcxFromFolder()
    {
        if (_ncx is null)
        {
            return;
        }

        NcxResource ncx = _ncx;
        UnwireResourceEvents(ncx);
        RemoveResource(ncx);
        ncx.Delete();
        _ncx = null;
    }

    /// <summary>Writes <c>META-INF/container.xml</c> pointing at the given OPF bookpath.</summary>
    public static void UpdateContainerXml(string mainFolderPath, string opfBookPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(mainFolderPath);
        ArgumentException.ThrowIfNullOrEmpty(opfBookPath);
        string metaInf = SysPath.Combine(mainFolderPath, "META-INF");
        Directory.CreateDirectory(metaInf);
        string template = ContainerXmlTemplate;
        Utility.WriteUnicodeTextFile(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, template, opfBookPath),
            SysPath.Combine(metaInf, "container.xml"));
    }

    // ---------------------------------------------------------------------
    //  Lookup
    // ---------------------------------------------------------------------

    /// <summary>The resource with the given identifier or <c>null</c>.</summary>
    public Resource? GetResourceByIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        lock (_accessLock)
        {
            return _idToResource.GetValueOrDefault(identifier);
        }
    }

    /// <summary>The resource at the given bookpath. Throws <see cref="KeyNotFoundException"/> when it does not exist.</summary>
    public Resource GetResourceByBookPath(string bookPath)
    {
        Resource? resource = GetResourceByBookPathNoThrow(bookPath);
        return resource ?? throw new KeyNotFoundException(CoreStrings.Format("Error_NoResourceAtBookPath", bookPath));
    }

    /// <summary>The resource at the given bookpath or <c>null</c>.</summary>
    public Resource? GetResourceByBookPathNoThrow(string bookPath)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        lock (_accessLock)
        {
            return _pathToResource.GetValueOrDefault(bookPath);
        }
    }

    /// <summary>
    /// Finds the bookpath of the resource whose path ends with <paramref name="pathEnd"/>
    /// (a case-insensitive comparison; the full file name must match).
    /// </summary>
    public string GetBookPathByPathEnd(string pathEnd)
    {
        ArgumentNullException.ThrowIfNull(pathEnd);
        string wantedName = pathEnd.Split('/').Last();
        lock (_accessLock)
        {
            foreach (Resource resource in _idToResource.Values)
            {
                string bookPath = resource.BookPath;
                if (bookPath.EndsWith(pathEnd, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(bookPath.Split('/').Last(), wantedName, StringComparison.OrdinalIgnoreCase))
                {
                    return bookPath;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>All resources in the registry (unspecified order).</summary>
    public IReadOnlyList<Resource> GetResourceList()
    {
        lock (_accessLock)
        {
            return _idToResource.Values.ToList();
        }
    }

    /// <summary>Resources of the given type.</summary>
    public IReadOnlyList<Resource> GetResourceListByType(ResourceType type)
    {
        lock (_accessLock)
        {
            return _idToResource.Values.Where(resource => resource.Type == type).ToList();
        }
    }

    /// <summary>Resources with any of the given MIME types.</summary>
    public IReadOnlyList<Resource> GetResourceListByMediaTypes(IEnumerable<string> mediaTypes)
    {
        ArgumentNullException.ThrowIfNull(mediaTypes);
        HashSet<string> wanted = new(mediaTypes, StringComparer.Ordinal);
        lock (_accessLock)
        {
            return _idToResource.Values.Where(resource => wanted.Contains(resource.MediaType)).ToList();
        }
    }

    /// <summary>
    /// Resources of type <typeparamref name="T"/>, optionally sorted.
    /// For <see cref="HtmlResource"/> the sort reflects spine order (if the OPF exists),
    /// for the others — the file name (ordinal). Files outside the spine go last.
    /// </summary>
    public IReadOnlyList<T> GetResourceTypeList<T>(bool sorted = false)
        where T : Resource
    {
        List<T> resources;
        lock (_accessLock)
        {
            resources = _idToResource.Values.OfType<T>().ToList();
        }

        if (!sorted)
        {
            return resources;
        }

        if (typeof(T) == typeof(HtmlResource) && _opf is not null)
        {
            IReadOnlyList<string> spineOrder = _opf.GetSpineOrderBookPaths();
            List<T> remaining = new(resources);
            List<T> ordered = new(resources.Count);
            foreach (string bookPath in spineOrder)
            {
                int index = remaining.FindIndex(
                    resource => string.Equals(resource.BookPath, bookPath, StringComparison.Ordinal));
                if (index >= 0)
                {
                    ordered.Add(remaining[index]);
                    remaining.RemoveAt(index);
                }
            }

            ordered.AddRange(remaining);
            return ordered;
        }

        return resources.OrderBy(resource => resource.Filename, StringComparer.Ordinal).ToList();
    }

    /// <summary>The highest reading order number (the number of HTML resources minus 1).</summary>
    public int GetHighestReadingOrder()
    {
        lock (_accessLock)
        {
            return _idToResource.Values.Count(resource => resource.Type == ResourceType.Html) - 1;
        }
    }

    /// <summary>The file names of all resources.</summary>
    public IReadOnlyList<string> GetAllFilenames()
    {
        lock (_accessLock)
        {
            return _idToResource.Values.Select(resource => resource.Filename).ToList();
        }
    }

    /// <summary>The bookpaths of all resources.</summary>
    public IReadOnlyList<string> GetAllBookPaths()
    {
        lock (_accessLock)
        {
            return _idToResource.Values.Select(resource => resource.BookPath).ToList();
        }
    }

    /// <summary>The resources referenced by the given bookpaths (unknown ones are skipped).</summary>
    public IReadOnlyList<Resource> GetLinkedResources(IEnumerable<string> bookPaths)
    {
        ArgumentNullException.ThrowIfNull(bookPaths);
        List<Resource> linked = new();
        foreach (string bookPath in bookPaths)
        {
            Resource? resource = GetResourceByBookPathNoThrow(bookPath);
            if (resource is not null)
            {
                linked.Add(resource);
            }
        }

        return linked;
    }

    /// <summary>
    /// Returns a version of the file name that is unique across the whole book: if the name is free — unchanged,
    /// otherwise a numeric suffix is appended.
    /// </summary>
    public string GetUniqueFilenameVersion(string filename) => UniqueFilenameVersion(filename, GetAllFilenames());

    /// <summary>
    /// <see cref="GetUniqueFilenameVersion(string)"/> against an explicit list of the file names in use
    /// (case-insensitive) — lets callers plan renames on a simulated state of the book.
    /// </summary>
    public static string UniqueFilenameVersion(string filename, IReadOnlyCollection<string> existing)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentNullException.ThrowIfNull(existing);
        if (!existing.Contains(filename, StringComparer.OrdinalIgnoreCase))
        {
            return filename;
        }

        string baseName = SysPath.GetFileNameWithoutExtension(filename);
        string namePrefix = TrailingDigits.Replace(baseName, string.Empty);
        string extension = GetCompleteSuffix(filename);

        string pattern = "^" + Regex.Escape(namePrefix) + "(\\d*)" +
                         (extension.Length > 0 ? "\\." + Regex.Escape(extension) : string.Empty) + "$";
        Regex search = new(pattern, RegexOptions.IgnoreCase);

        int maxNum = -1;
        int maxNumLength = -1;
        foreach (string candidate in existing)
        {
            Match match = search.Match(candidate);
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups[1].Value, out int suffix) && suffix > maxNum)
            {
                maxNum = suffix;
                maxNumLength = match.Groups[1].Length;
            }
        }

        if (maxNum == -1)
        {
            maxNum = 0;
            maxNumLength = 4;
        }

        string newName = namePrefix + (maxNum + 1).ToString(
            "D" + Math.Max(1, maxNumLength).ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture);
        return extension.Length > 0 ? newName + "." + extension : newName;
    }

    // ---------------------------------------------------------------------
    //  Removing / renaming / moving
    // ---------------------------------------------------------------------

    /// <summary>Unregisters a resource from the maps and raises <see cref="ResourceRemoved"/>.</summary>
    public void RemoveResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _opf?.RemoveResource(resource);
        lock (_accessLock)
        {
            _idToResource.Remove(resource.Identifier);
            _pathToResource.Remove(resource.BookPath);
            _watchedFiles.Remove(resource.FullPath);
            _suspendedWatchedFiles.RemoveAll(path => string.Equals(path, resource.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        RefreshWatcherState();
        ResourceRemoved?.Invoke(this, new ResourceEventArgs(resource));
    }

    /// <summary>Removes many resources (from the maps, the watchers and the OPF manifest) and deletes their files.</summary>
    public void BulkRemoveResources(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        List<Resource> toRemove = resources.ToList();
        _opf?.BulkRemoveResources(toRemove);
        foreach (Resource resource in toRemove)
        {
            lock (_accessLock)
            {
                _idToResource.Remove(resource.Identifier);
                _pathToResource.Remove(resource.BookPath);
                _watchedFiles.Remove(resource.FullPath);
                _suspendedWatchedFiles.RemoveAll(path => string.Equals(path, resource.FullPath, StringComparison.OrdinalIgnoreCase));
            }

            UnwireResourceEvents(resource);
            resource.Delete();
            ResourceRemoved?.Invoke(this, new ResourceEventArgs(resource));
        }

        RefreshWatcherState();
    }

    /// <summary>Removes a resource without notifying the OPF and deletes its file.</summary>
    public void RemoveWithoutUpdatingOpf(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_accessLock)
        {
            _idToResource.Remove(resource.Identifier);
            _pathToResource.Remove(resource.BookPath);
            _watchedFiles.Remove(resource.FullPath);
            _suspendedWatchedFiles.RemoveAll(path => string.Equals(path, resource.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        UnwireResourceEvents(resource);
        RefreshWatcherState();
        resource.Delete();
    }

    /// <summary>Renames many resources at once and updates the maps, the OPF manifest and the short names.</summary>
    public void BulkRenameResources(IReadOnlyList<Resource> resources, IReadOnlyList<string> newFilenames)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(newFilenames);
        if (resources.Count != newFilenames.Count)
        {
            throw new ArgumentException(CoreStrings.Format("Error_CountMismatch", "resources/names"), nameof(newFilenames));
        }

        // Renaming each resource raises the Renamed event -> the maps and OPF are updated
        // in OnResourceRenamed / HandlePathChange.
        for (int i = 0; i < resources.Count; i++)
        {
            resources[i].RenameTo(newFilenames[i]);
        }

        UpdateShortPathNames();
    }

    /// <summary>Moves many resources to new bookpaths and updates the maps, the OPF manifest and the short names.</summary>
    public void BulkMoveResources(IReadOnlyList<Resource> resources, IReadOnlyList<string> newBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(newBookPaths);
        if (resources.Count != newBookPaths.Count)
        {
            throw new ArgumentException(CoreStrings.Format("Error_CountMismatch", "resources/paths"), nameof(newBookPaths));
        }

        for (int i = 0; i < resources.Count; i++)
        {
            resources[i].MoveTo(newBookPaths[i]);
        }

        UpdateShortPathNames();
    }

    // ---------------------------------------------------------------------
    //  Groups -> folders
    // ---------------------------------------------------------------------

    /// <summary>The folders assigned to a group (at least <c>[""]</c>).</summary>
    public IReadOnlyList<string> GetFoldersForGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        CreateGroupToFoldersMap();
        return _groupToFolders.TryGetValue(group, out List<string>? folders) && folders.Count > 0
            ? folders
            : new List<string> { string.Empty };
    }

    /// <summary>The group's default (first) folder.</summary>
    public string GetDefaultFolderForGroup(string group) => GetFoldersForGroup(group)[0];

    /// <summary>The group's folder in the Signet standard form (e.g. <c>OEBPS/Text</c>).</summary>
    public string GetStdFolderForGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        CreateStdGroupToFoldersMap();
        return _stdGroupToFolders.TryGetValue(group, out List<string>? folders) && folders.Count > 0
            ? folders[0]
            : string.Empty;
    }

    /// <summary>Overwrites the group's folder list.</summary>
    public void SetFoldersForGroup(string group, IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(folders);
        CreateGroupToFoldersMap();
        _groupToFolders[group] = folders.ToList();
    }

    /// <summary>
    /// Rebuilds the group -&gt; folders map from the locations actually in use
    /// (the bookpath + MIME type of each resource, skipping <c>META-INF</c>).
    /// </summary>
    /// <param name="bookPaths">The resources' bookpaths.</param>
    /// <param name="mediaTypes">The resources' MIME types (parallel to <paramref name="bookPaths"/>).</param>
    /// <param name="updateOnly">When <c>true</c>, the existing group folders are kept (they are not collapsed to empty ones).</param>
    public void SetGroupFolders(IReadOnlyList<string> bookPaths, IReadOnlyList<string> mediaTypes, bool updateOnly = false)
    {
        ArgumentNullException.ThrowIfNull(bookPaths);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        if (bookPaths.Count != mediaTypes.Count)
        {
            throw new ArgumentException(CoreStrings.Format("Error_CountMismatch", "paths/media types"), nameof(mediaTypes));
        }

        Dictionary<string, List<string>> groupFolder = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> groupCount = new(StringComparer.Ordinal);

        for (int i = 0; i < bookPaths.Count; i++)
        {
            string bookPath = bookPaths[i];
            if (bookPath.StartsWith("META-INF", StringComparison.Ordinal))
            {
                continue;
            }

            string group = MediaTypes.GetGroupFromMediaType(mediaTypes[i], "other");
            string startDir = Core.BookPath.StartingDir(bookPath);

            List<string> folders = groupFolder.TryGetValue(group, out List<string>? f) ? f : groupFolder[group] = new List<string>();
            List<int> counts = groupCount.TryGetValue(group, out List<int>? c) ? c : groupCount[group] = new List<int>();

            int pos = folders.IndexOf(startDir);
            if (pos < 0)
            {
                folders.Add(startDir);
                counts.Add(1);
            }
            else
            {
                counts[pos]++;
            }
        }

        List<string> dominantDirs = new();
        bool useLowerCase = false;
        foreach (string group in groupFolder.Keys.ToList())
        {
            IReadOnlyList<string> sorted = Utility.SortByCounts(groupFolder[group], groupCount[group]);
            groupFolder[group] = sorted.ToList();
            if (GroupB.Contains(group) && sorted[0].Contains(group.ToLowerInvariant(), StringComparison.Ordinal))
            {
                useLowerCase = true;
            }

            dominantDirs.Add(sorted[0]);
        }

        if (updateOnly)
        {
            foreach (string group in GroupA)
            {
                List<string> folders = groupFolder.TryGetValue(group, out List<string>? f) ? f : new List<string>();
                foreach (string folder in GetFoldersForGroup(group))
                {
                    if (!folders.Contains(folder))
                    {
                        folders.Add(folder);
                    }
                }

                groupFolder[group] = folders;
            }
        }
        else
        {
            string commonBase = Core.BookPath.LongestCommonPath(dominantDirs);
            if (commonBase == "/")
            {
                commonBase = string.Empty;
            }

            foreach (string group in GroupA)
            {
                List<string> folders = groupFolder.TryGetValue(group, out List<string>? f) ? f : new List<string>();
                if (folders.Count == 0)
                {
                    string groupName = useLowerCase ? group.ToLowerInvariant() : group;
                    folders.Add(commonBase + groupName);
                    groupFolder[group] = folders;
                }
            }
        }

        _groupToFolders.Clear();
        foreach ((string group, List<string> folders) in groupFolder)
        {
            _groupToFolders[group] = folders;
        }
    }

    /// <summary>Rebuilds the group -&gt; folders map from the registry's current contents (<c>updateOnly</c> mode).</summary>
    public void RefreshGroupFolders()
    {
        List<string> bookPaths = new();
        List<string> mediaTypes = new();
        foreach (Resource resource in GetResourceList())
        {
            if (resource.BookPath.StartsWith("META-INF", StringComparison.Ordinal))
            {
                continue;
            }

            bookPaths.Add(resource.BookPath);
            mediaTypes.Add(resource.MediaType);
        }

        SetGroupFolders(bookPaths, mediaTypes, updateOnly: true);
    }

    /// <summary>
    /// Whether the directory layout matches the "Signet standard form" (<c>OEBPS/content.opf</c>,
    /// <c>OEBPS/toc.ncx</c>, one <c>OEBPS/&lt;Group&gt;</c> folder per group).
    /// </summary>
    public bool EpubInSignetStandardForm()
    {
        if (_opf is null)
        {
            return false;
        }

        bool standard = _opf.BookPath == "OEBPS/content.opf";
        if (_ncx is not null)
        {
            standard = standard && _ncx.BookPath == "OEBPS/toc.ncx";
        }

        if (!standard)
        {
            return false;
        }

        string[] groups = { "Text", "Styles", "Fonts", "Images", "Audio", "Video", "Misc" };
        foreach (string group in groups)
        {
            IReadOnlyList<string> folders = GetFoldersForGroup(group);
            standard = standard && folders.Count == 1 && folders[0] == "OEBPS/" + group;
        }

        return standard;
    }

    /// <summary>
    /// Recomputes the short path names (<see cref="Resource.ShortPathName"/>) so that they are
    /// unique — starting from the bare file name and appending further directory segments where
    /// collisions occur.
    /// </summary>
    public void UpdateShortPathNames()
    {
        IReadOnlyList<string> bookPaths = GetAllBookPaths();

        Dictionary<string, string> bookToShort = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> nameToBooks = new(StringComparer.Ordinal);
        HashSet<string> duplicates = new(StringComparer.Ordinal);
        int level = 1;

        foreach (string bookPath in bookPaths)
        {
            string name = BuildShortName(bookPath, level);
            bookToShort[bookPath] = name;
            if (nameToBooks.TryGetValue(name, out List<string>? books))
            {
                duplicates.Add(name);
                books.Add(bookPath);
            }
            else
            {
                nameToBooks[name] = new List<string> { bookPath };
            }
        }

        List<string> todo = duplicates.ToList();
        while (todo.Count > 0)
        {
            duplicates.Clear();
            level++;
            foreach (string name in todo)
            {
                List<string> books = nameToBooks[name];
                nameToBooks.Remove(name);
                foreach (string bookPath in books)
                {
                    string newName = BuildShortName(bookPath, level);
                    bookToShort[bookPath] = newName;
                    if (nameToBooks.TryGetValue(newName, out List<string>? existing))
                    {
                        duplicates.Add(newName);
                        existing.Add(bookPath);
                    }
                    else
                    {
                        nameToBooks[newName] = new List<string> { bookPath };
                    }
                }
            }

            todo = duplicates.ToList();
        }

        foreach (string bookPath in bookPaths)
        {
            Resource? resource = GetResourceByBookPathNoThrow(bookPath);
            if (resource is null)
            {
                continue;
            }

            string shortName = bookToShort[bookPath];
            if (shortName.StartsWith('^'))
            {
                shortName = shortName[1..];
            }

            if (resource.ShortPathName != shortName)
            {
                resource.ShortPathName = shortName;
            }
        }
    }

    /// <summary>Loads from disk the content of every text resource that is not HTML.</summary>
    public void PerformInitialLoads()
    {
        foreach (Resource resource in GetResourceList())
        {
            if (resource.Type == ResourceType.Html)
            {
                continue;
            }

            if (resource is TextResource textResource)
            {
                textResource.InitialLoad();
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Watching files on disk
    // ---------------------------------------------------------------------

    /// <summary>Registers a resource file to be watched for external changes.</summary>
    public void WatchResourceFile(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!_fileWatchingEnabled)
        {
            return;
        }

        lock (_accessLock)
        {
            _watchedFiles.Add(resource.FullPath);
        }

        RefreshWatcherState();
    }

    /// <summary>Suspends watching of all files (for the duration of a book save).</summary>
    public void SuspendWatchingResources()
    {
        lock (_accessLock)
        {
            if (_suspendedWatchedFiles.Count == 0 && _watchedFiles.Count > 0)
            {
                _suspendedWatchedFiles.AddRange(_watchedFiles);
                _watchedFiles.Clear();
            }
        }

        RefreshWatcherState();
    }

    /// <summary>Resumes file watching suspended by <see cref="SuspendWatchingResources"/>.</summary>
    public void ResumeWatchingResources()
    {
        lock (_accessLock)
        {
            if (_suspendedWatchedFiles.Count > 0)
            {
                foreach (string path in _suspendedWatchedFiles)
                {
                    if (File.Exists(path))
                    {
                        _watchedFiles.Add(path);
                    }
                }

                _suspendedWatchedFiles.Clear();
            }
        }

        RefreshWatcherState();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_fsWatcher is not null)
        {
            _fsWatcher.EnableRaisingEvents = false;
            _fsWatcher.Changed -= OnWatchedFileChanged;
            _fsWatcher.Created -= OnWatchedFileChanged;
            _fsWatcher.Dispose();
            _fsWatcher = null;
        }

        foreach (Resource resource in _idToResource.Values.ToList())
        {
            UnwireResourceEvents(resource);
            resource.Dispose();
        }

        _idToResource.Clear();
        _pathToResource.Clear();
        if (_ownsTempFolder)
        {
            _tempFolder.Dispose();
        }
    }

    /// <summary>
    /// Hands ownership of the working folder over to the caller: from now on <see cref="Dispose"/> does not
    /// delete the folder, and the returned <see cref="TempFolder"/> is the caller's to release. Used
    /// by checkpoints (<see cref="CheckpointHistory"/>), which keep the book's state folders
    /// for longer than a single <see cref="Book"/> object lives.
    /// </summary>
    /// <returns>This registry's working folder.</returns>
    public TempFolder ReleaseTempFolderOwnership()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownsTempFolder = false;
        return _tempFolder;
    }

    // ---------------------------------------------------------------------
    //  Implementation details
    // ---------------------------------------------------------------------

    internal const string OpfMediaType = "application/oebps-package+xml";
    internal const string NcxMediaType = "application/x-dtbncx+xml";

    private string ToSystemPath(string bookPath) =>
        SysPath.Combine(MainFolderPath, bookPath.Replace('/', SysPath.DirectorySeparatorChar));

    private void CreateGroupToFoldersMap()
    {
        if (_groupToFolders.Count > 0)
        {
            return;
        }

        _groupToFolders["Text"] = new List<string> { "OEBPS/Text" };
        _groupToFolders["Styles"] = new List<string> { "OEBPS/Styles" };
        _groupToFolders["Images"] = new List<string> { "OEBPS/Images" };
        _groupToFolders["Fonts"] = new List<string> { "OEBPS/Fonts" };
        _groupToFolders["Audio"] = new List<string> { "OEBPS/Audio" };
        _groupToFolders["Video"] = new List<string> { "OEBPS/Video" };
        _groupToFolders["Misc"] = new List<string> { "OEBPS/Misc" };
        _groupToFolders["ncx"] = new List<string> { "OEBPS" };
        _groupToFolders["opf"] = new List<string> { "OEBPS" };
        _groupToFolders["other"] = new List<string> { string.Empty };
    }

    private void CreateStdGroupToFoldersMap()
    {
        if (_stdGroupToFolders.Count > 0)
        {
            return;
        }

        _stdGroupToFolders["Text"] = new List<string> { "OEBPS/Text" };
        _stdGroupToFolders["Styles"] = new List<string> { "OEBPS/Styles" };
        _stdGroupToFolders["Images"] = new List<string> { "OEBPS/Images" };
        _stdGroupToFolders["Fonts"] = new List<string> { "OEBPS/Fonts" };
        _stdGroupToFolders["Audio"] = new List<string> { "OEBPS/Audio" };
        _stdGroupToFolders["Video"] = new List<string> { "OEBPS/Video" };
        _stdGroupToFolders["Misc"] = new List<string> { "OEBPS/Misc" };
        _stdGroupToFolders["ncx"] = new List<string> { "OEBPS" };
        _stdGroupToFolders["opf"] = new List<string> { "OEBPS" };
        _stdGroupToFolders["other"] = new List<string> { string.Empty };
    }

    private static string BuildShortName(string bookPath, int level)
    {
        string[] pieces = bookPath.Split('/');
        if (level == 1)
        {
            return pieces[^1];
        }

        if (level >= pieces.Length)
        {
            return "^" + bookPath;
        }

        // Keep the last `level` segments.
        return string.Join('/', pieces.Skip(pieces.Length - level));
    }

    private static string GetCompleteSuffix(string filename)
    {
        int firstDot = filename.IndexOf('.');
        return firstDot >= 0 && firstDot < filename.Length - 1 ? filename[(firstDot + 1)..] : string.Empty;
    }

    private void WireResourceEvents(Resource resource)
    {
        resource.Deleted += OnResourceDeleted;
        resource.Renamed += OnResourceRenamed;
        resource.Moved += OnResourceMoved;
        resource.ResourceUpdatedFromDisk += OnResourceUpdatedFromDisk;
    }

    private void UnwireResourceEvents(Resource resource)
    {
        resource.Deleted -= OnResourceDeleted;
        resource.Renamed -= OnResourceRenamed;
        resource.Moved -= OnResourceMoved;
        resource.ResourceUpdatedFromDisk -= OnResourceUpdatedFromDisk;
    }

    private void OnResourceDeleted(object? sender, EventArgs e)
    {
        if (sender is Resource resource)
        {
            RemoveResource(resource);
        }
    }

    private void OnResourceRenamed(object? sender, ResourcePathChangedEventArgs e)
    {
        HandlePathChange(sender as Resource, e.OldFullPath, renamed: true);
    }

    private void OnResourceMoved(object? sender, ResourcePathChangedEventArgs e)
    {
        HandlePathChange(sender as Resource, e.OldFullPath, renamed: false);
    }

    private void OnResourceUpdatedFromDisk(object? sender, EventArgs e)
    {
        if (sender is Resource resource)
        {
            ResourceFileChangedOnDisk?.Invoke(this, new ResourceEventArgs(resource));
        }
    }

    private void HandlePathChange(Resource? resource, string oldFullPath, bool renamed)
    {
        if (resource is null)
        {
            return;
        }

        string oldBookPath = oldFullPath.Length > MainFolderPath.Length
            ? oldFullPath[(MainFolderPath.Length + 1)..].Replace('\\', '/')
            : oldFullPath.Replace('\\', '/');

        lock (_accessLock)
        {
            if (_pathToResource.TryGetValue(oldBookPath, out Resource? tracked))
            {
                _pathToResource.Remove(oldBookPath);
                _pathToResource[resource.BookPath] = tracked;
            }
        }

        if (!ReferenceEquals(resource, _opf))
        {
            if (renamed)
            {
                _opf?.ResourceRenamed(resource, oldFullPath);
            }
            else
            {
                _opf?.ResourceMoved(resource, oldFullPath);
            }
        }
        else
        {
            // Moving / renaming the OPF itself requires rewriting META-INF/container.xml — and, for a move, rebasing
            // the manifest hrefs, which are relative to the OPF.
            UpdateContainerXml(MainFolderPath, resource.BookPath);
            if (!renamed)
            {
                _opf?.OpfMoved(oldBookPath);
            }
        }

        UpdateShortPathNames();
    }

    private void RefreshWatcherState()
    {
        if (!_fileWatchingEnabled)
        {
            return;
        }

        if (_fsWatcher is null)
        {
            _fsWatcher = new FileSystemWatcher(MainFolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _fsWatcher.Changed += OnWatchedFileChanged;
            _fsWatcher.Created += OnWatchedFileChanged;
        }

        bool shouldWatch;
        lock (_accessLock)
        {
            shouldWatch = _watchedFiles.Count > 0;
        }

        _fsWatcher.EnableRaisingEvents = shouldWatch && !_disposed;
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        Resource? target = null;
        lock (_accessLock)
        {
            if (!_watchedFiles.Contains(e.FullPath))
            {
                return;
            }

            target = _idToResource.Values.FirstOrDefault(
                resource => string.Equals(resource.FullPath, e.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        if (target is not null && File.Exists(e.FullPath))
        {
            ResourceFileChangedOnDisk?.Invoke(this, new ResourceEventArgs(target));
        }
    }
}
