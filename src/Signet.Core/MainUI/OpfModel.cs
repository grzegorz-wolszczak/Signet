using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.SourceUpdates;
using Signet.Core.Toc;
using Signet.Core.Localization;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.MainUI;

/// <summary>
/// The hierarchical model of the publication's resources for the Book Browser panel,
/// with no dependency on view classes.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>The model is a pure data structure (a tree of <see cref="OpfModelFolder"/> →
///   <see cref="OpfModelEntry"/>); mapping to a <c>TreeView</c> control belongs to the App layer.</item>
///   <item>(X)HTML files are sorted by spine order (files outside the spine — last);
///   the other groups alphabetically by display name.</item>
///   <item>Reordering Text (<see cref="ReorderText"/>/<see cref="MoveText"/>) updates the
///   spine via <see cref="Resources.OpfResource.UpdateSpineOrder"/> and marks the book as
///   modified. Mouse dragging is handled by the App layer.</item>
///   <item>Reacts to <see cref="FolderKeeper.ResourceAdded"/>/<see cref="FolderKeeper.ResourceRemoved"/>
///   and <see cref="Resource.Renamed"/>/<see cref="Resource.Moved"/> — after each of these events
///   it rebuilds the tree and raises <see cref="Changed"/>.</item>
/// </list>
/// </remarks>
public sealed class OpfModel : IDisposable
{
    private readonly Book _book;
    private readonly FolderKeeper _folderKeeper;
    private readonly bool _showFullPath;
    private readonly List<OpfModelFolder> _folders = new();
    private readonly List<OpfModelEntry> _topLevel = new();
    private readonly List<Resource> _watchedResources = new();
    private bool _disposed;
    private bool _suppressRefresh;

    /// <summary>Creates a model for the given book and builds the tree right away.</summary>
    /// <param name="book">The book whose resources the model represents.</param>
    /// <param name="showFullPath">When <c>true</c>, entries show the full bookpath instead of the short name.</param>
    public OpfModel(Book book, bool showFullPath = false)
    {
        ArgumentNullException.ThrowIfNull(book);
        _book = book;
        _folderKeeper = book.GetFolderKeeper();
        _showFullPath = showFullPath;

        _folderKeeper.ResourceAdded += OnFolderKeeperChanged;
        _folderKeeper.ResourceRemoved += OnFolderKeeperChanged;

        Refresh();
    }

    /// <summary>Raised after every tree rebuild (resource added/removed/renamed, reorder).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The file groups (Text, Styles, Images, Fonts, Audio, Video, Misc) in a fixed order —
    /// always all seven, including empty ones (permanent folders).
    /// </summary>
    public IReadOnlyList<OpfModelFolder> Folders => _folders;

    /// <summary>Files without a group (OPF, NCX) — items attached directly to the root.</summary>
    public IReadOnlyList<OpfModelEntry> TopLevelFiles => _topLevel;

    /// <summary>Returns the group of the given kind (<c>null</c> only after <see cref="Dispose"/>/before the tree is built).</summary>
    public OpfModelFolder? GetFolder(OpfModelGroupKind kind) =>
        _folders.FirstOrDefault(f => f.Kind == kind);

    /// <summary>All tree entries (groups + OPF/NCX), in display order.</summary>
    public IReadOnlyList<OpfModelEntry> AllEntries()
    {
        List<OpfModelEntry> all = new();
        foreach (OpfModelFolder folder in _folders)
        {
            all.AddRange(folder.Entries);
        }

        all.AddRange(_topLevel);
        return all;
    }

    /// <summary>Forces a full rebuild of the tree from the book's current state.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        RewatchResources();

        IReadOnlyList<Resource> resources = _folderKeeper.GetResourceList();
        OpfResource opf = _book.GetOpf();
        string version = opf.EpubVersion;
        bool epub3 = version.StartsWith('3');

        IReadOnlyDictionary<Resource, int> readingOrders = opf.GetReadingOrderAll(resources);
        IReadOnlyDictionary<string, string> manifestProps =
            epub3 ? opf.GetManifestPropertiesForPaths() : EmptyStringMap;
        Dictionary<string, IReadOnlyList<string>> semanticByPath = BuildSemanticMap(opf, epub3);
        string navBookPath = epub3 ? opf.GetNavResourceBookPath() : string.Empty;
        string coverImagePath = SafeCoverImagePath(opf);

        Dictionary<OpfModelGroupKind, List<OpfModelEntry>> groups = new();
        _topLevel.Clear();

        foreach (Resource resource in resources)
        {
            OpfModelEntry entry = new(resource, DisplayNameFor(resource));

            entry.ReadingOrder = readingOrders.TryGetValue(resource, out int order) ? order : -1;
            entry.ManifestProperties = manifestProps.GetValueOrDefault(resource.BookPath, string.Empty);
            entry.SemanticTypes = semanticByPath.GetValueOrDefault(resource.BookPath, Array.Empty<string>());
            entry.IsNav = navBookPath.Length > 0 &&
                          string.Equals(resource.BookPath, navBookPath, StringComparison.Ordinal);
            entry.IsCover = coverImagePath.Length > 0 &&
                            string.Equals(resource.BookPath, coverImagePath, StringComparison.Ordinal);
            entry.IsWellFormed = resource is HtmlResource html ? CheckWellFormed(html) : null;
            entry.SortKey = SortKeyFor(entry.DisplayName);
            entry.ToolTip = BuildToolTip(entry);

            if (resource.Type is ResourceType.Opf or ResourceType.Ncx)
            {
                _topLevel.Add(entry);
                continue;
            }

            OpfModelGroupKind kind = GroupFor(resource.Type);
            if (!groups.TryGetValue(kind, out List<OpfModelEntry>? list))
            {
                list = groups[kind] = new List<OpfModelEntry>();
            }

            list.Add(entry);
        }

        _topLevel.Sort(static (a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

        _folders.Clear();
        foreach (OpfModelGroupKind kind in GroupOrder)
        {
            if (!groups.TryGetValue(kind, out List<OpfModelEntry>? list))
            {
                list = new List<OpfModelEntry>();
            }

            if (kind == OpfModelGroupKind.Text)
            {
                list.Sort(static (a, b) => SpineRank(a).CompareTo(SpineRank(b)));
            }
            else
            {
                list.Sort(static (a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            }

            string groupName = GroupName(kind);
            _folders.Add(new OpfModelFolder(kind, groupName, _folderKeeper.GetDefaultFolderForGroup(groupName), list));
        }

        if (!_suppressRefresh)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sets a new order of the Text files (the full list of the Text group's entries in the given order)
    /// and rewrites the spine.
    /// </summary>
    /// <returns><c>true</c> when the order was actually updated.</returns>
    public bool ReorderText(IReadOnlyList<OpfModelEntry> newOrder)
    {
        ArgumentNullException.ThrowIfNull(newOrder);
        OpfModelFolder? text = GetFolder(OpfModelGroupKind.Text);
        if (text is null)
        {
            return false;
        }

        HashSet<string> expected = text.Entries.Select(e => e.Identifier).ToHashSet(StringComparer.Ordinal);
        HashSet<string> provided = newOrder.Select(e => e.Identifier).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(provided))
        {
            throw new ArgumentException(
                "The new order must contain exactly the same Text files as the current one.", nameof(newOrder));
        }

        OpfResource opf = _book.GetOpf();
        string navBookPath = opf.EpubVersion.StartsWith('3') ? opf.GetNavResourceBookPath() : string.Empty;
        HashSet<string> currentSpine = opf.GetSpineOrderBookPaths().ToHashSet(StringComparer.Ordinal);

        // A navigation document that is not in the spine keeps its position in the Text folder,
        // but does not go into the spine.
        List<HtmlResource> htmls = newOrder
            .Select(e => e.Resource)
            .OfType<HtmlResource>()
            .Where(h => currentSpine.Contains(h.BookPath) ||
                        !string.Equals(h.BookPath, navBookPath, StringComparison.Ordinal))
            .ToList();

        opf.UpdateSpineOrder(htmls);
        _book.Modified = true;

        _suppressRefresh = true;
        try
        {
            Refresh();
        }
        finally
        {
            _suppressRefresh = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves a single Text file by <paramref name="delta"/> positions (negative = up).</summary>
    /// <returns><c>true</c> when the move was performed.</returns>
    public bool MoveText(OpfModelEntry entry, int delta)
    {
        ArgumentNullException.ThrowIfNull(entry);
        OpfModelFolder? text = GetFolder(OpfModelGroupKind.Text);
        if (text is null || delta == 0)
        {
            return false;
        }

        List<OpfModelEntry> order = text.Entries.ToList();
        int index = order.FindIndex(e => e.Identifier == entry.Identifier);
        if (index < 0)
        {
            return false;
        }

        int target = Math.Clamp(index + delta, 0, order.Count - 1);
        if (target == index)
        {
            return false;
        }

        order.RemoveAt(index);
        order.Insert(target, entry);
        return ReorderText(order);
    }

    /// <summary>
    /// Sorts the given Text files alphanumerically by name and rewrites the spine
    /// (sorts the chosen subset "in place", keeping the positions of the remaining files).
    /// </summary>
    public bool SortTextByFilename(IEnumerable<OpfModelEntry> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        OpfModelFolder? text = GetFolder(OpfModelGroupKind.Text);
        if (text is null)
        {
            return false;
        }

        HashSet<string> pick = selected.Select(e => e.Identifier).ToHashSet(StringComparer.Ordinal);
        List<OpfModelEntry> current = text.Entries.ToList();
        List<int> slots = new();
        List<OpfModelEntry> chosen = new();
        for (int i = 0; i < current.Count; i++)
        {
            if (pick.Contains(current[i].Identifier))
            {
                slots.Add(i);
                chosen.Add(current[i]);
            }
        }

        if (chosen.Count < 2)
        {
            return false;
        }

        chosen.Sort(static (a, b) => AlphanumericComparer.Instance.Compare(a.SortKey, b.SortKey));
        for (int i = 0; i < slots.Count; i++)
        {
            current[slots[i]] = chosen[i];
        }

        return ReorderText(current);
    }

    /// <summary>
    /// Renames the file of the given resource and updates all references (<c>href</c>/<c>src</c>/
    /// <c>url(...)</c>) to it across the whole publication (a full rename with link updates).
    /// </summary>
    /// <param name="resource">The resource being renamed.</param>
    /// <param name="newFilename">The new name (with or without an extension — without one the old extension is kept).</param>
    /// <param name="error">The error message when the rename failed; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the rename succeeded (also when the name did not actually change).</returns>
    public bool RenameResource(Resource resource, string newFilename, out string? error)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFilename);

        string oldBookPath = resource.BookPath;
        if (oldBookPath.StartsWith("META-INF/", StringComparison.Ordinal))
        {
            error = CoreStrings.Get("Rename_MetaInfNotAllowed");
            return false;
        }

        string oldFilename = resource.Filename;
        string extension = ExtensionWithDot(oldFilename);
        string newFilenameWithExtension = newFilename.Contains('.', StringComparison.Ordinal)
            ? newFilename
            : newFilename + extension;

        if (string.Equals(oldFilename, newFilenameWithExtension, StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        if (!IsFilenameValid(oldBookPath, newFilenameWithExtension, out error))
        {
            return false;
        }

        Resource[] toRename = { resource };
        string[] newNames = { newFilenameWithExtension };
        _folderKeeper.BulkRenameResources(toRename, newNames);

        if (string.Equals(resource.BookPath, oldBookPath, StringComparison.Ordinal))
        {
            error = CoreStrings.Get("Rename_Failed");
            return false;
        }

        resource.CurrentBookRelPath = oldBookPath;
        Dictionary<string, string> updates = new(StringComparer.Ordinal) { [oldBookPath] = resource.BookPath };
        UniversalUpdates.Perform(_book, updates);
        _book.Modified = true;

        error = null;
        return true;
    }

    /// <summary>
    /// Renames many resources at once and updates the references across the whole publication in one operation
    /// (used by "Rename with a template" and "Bulk rename regex"). It has a safeguard: before actually applying
    /// the changes it simulates the recomputation of every HTML file and rejects the <em>whole</em> operation — with no
    /// mutation of the book — if a file that was well-formed before the operation would stop being so afterwards.
    /// </summary>
    /// <param name="resources">The resources to rename.</param>
    /// <param name="newFilenames">The new names, parallel to <paramref name="resources"/>.</param>
    /// <param name="notRenamed">The bookpaths of resources whose names could not be changed (invalid name, collision, META-INF).</param>
    /// <param name="wellFormedErrors">
    /// The bookpaths of HTML files that would stop being well-formed after the simulation — when non-empty, the operation
    /// was rejected entirely (nothing was changed).
    /// </param>
    /// <returns><c>true</c> when all the requested renames succeeded.</returns>
    public bool RenameResourceList(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<string> newFilenames,
        out IReadOnlyList<string> notRenamed,
        out IReadOnlyList<string> wellFormedErrors)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(newFilenames);
        if (resources.Count != newFilenames.Count)
        {
            throw new ArgumentException(CoreStrings.Format("Error_CountMismatch", "resources/names"), nameof(newFilenames));
        }

        List<string> failed = new();
        List<Resource> toRename = new();
        List<string> toRenameNewNames = new();
        List<string> toRenameOldBookPaths = new();
        Dictionary<string, string> updates = new(StringComparer.Ordinal);

        for (int i = 0; i < resources.Count; i++)
        {
            Resource resource = resources[i];
            string oldBookPath = resource.BookPath;
            if (oldBookPath.StartsWith("META-INF/", StringComparison.Ordinal))
            {
                failed.Add(oldBookPath);
                continue;
            }

            string oldFilename = resource.Filename;
            string extension = ExtensionWithDot(oldFilename);
            string newFilenameWithExtension = newFilenames[i].Contains('.', StringComparison.Ordinal)
                ? newFilenames[i]
                : newFilenames[i] + extension;

            if (string.Equals(oldFilename, newFilenameWithExtension, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsFilenameValid(oldBookPath, newFilenameWithExtension, out _))
            {
                failed.Add(oldBookPath);
                continue;
            }

            string startDir = CoreBookPath.StartingDir(oldBookPath);
            string newBookPath = startDir.Length == 0 ? newFilenameWithExtension : startDir + "/" + newFilenameWithExtension;

            toRename.Add(resource);
            toRenameNewNames.Add(newFilenameWithExtension);
            toRenameOldBookPaths.Add(oldBookPath);
            updates[oldBookPath] = newBookPath;
        }

        if (toRename.Count == 0)
        {
            notRenamed = failed;
            wellFormedErrors = Array.Empty<string>();
            return failed.Count == 0;
        }

        List<string> malformed = SimulateAndFindNewlyMalformedHtml(updates);
        if (malformed.Count > 0)
        {
            notRenamed = failed;
            wellFormedErrors = malformed;
            return false;
        }

        _folderKeeper.BulkRenameResources(toRename, toRenameNewNames);

        for (int i = 0; i < toRename.Count; i++)
        {
            Resource resource = toRename[i];
            string oldBookPath = toRenameOldBookPaths[i];
            if (string.Equals(resource.BookPath, oldBookPath, StringComparison.Ordinal))
            {
                failed.Add(oldBookPath);
                continue;
            }

            resource.CurrentBookRelPath = oldBookPath;
        }

        UniversalUpdates.Perform(_book, updates);
        _book.Modified = true;

        notRenamed = failed;
        wellFormedErrors = Array.Empty<string>();
        return failed.Count == 0;
    }

    /// <summary>
    /// Moves the given resources to a shared target folder and updates all references
    /// (<c>href</c>/<c>src</c>/<c>url(...)</c>) to them across the whole publication. Includes
    /// path validation, collision detection and registering the new folder in the group.
    /// </summary>
    /// <param name="resources">The resources to move (of one media-type group — the target folder is shared).</param>
    /// <param name="folderPath">
    /// The target folder as a folder bookpath (without a leading/trailing <c>/</c>); an empty string
    /// means the EPUB root (the <c>&lt;epub root&gt;</c> placeholder).
    /// </param>
    /// <param name="error">The error message when the operation failed; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when all resources were moved (also when no move was needed).</returns>
    public bool MoveResourceList(IReadOnlyList<Resource> resources, string folderPath, out string? error)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(folderPath);
        if (resources.Count == 0)
        {
            error = null;
            return true;
        }

        string normalizedFolder = folderPath.Trim().TrimEnd('/');
        if (!IsFolderPathValid(normalizedFolder))
        {
            error = CoreStrings.Format("Move_InvalidFolder", folderPath);
            return false;
        }

        foreach (Resource resource in resources)
        {
            if (resource.BookPath.StartsWith("META-INF/", StringComparison.Ordinal))
            {
                error = CoreStrings.Get("Move_MetaInfNotAllowed");
                return false;
            }
        }

        List<string> oldBookPaths = resources.Select(r => r.BookPath).ToList();
        HashSet<string> existing = _folderKeeper.GetAllBookPaths().ToHashSet(StringComparer.Ordinal);
        HashSet<string> proposed = new(StringComparer.Ordinal);
        List<string> newBookPaths = new();

        foreach (string oldBookPath in oldBookPaths)
        {
            string filename = oldBookPath[(oldBookPath.LastIndexOf('/') + 1)..];
            string newBookPath = normalizedFolder.Length == 0 ? filename : normalizedFolder + "/" + filename;
            bool collides = (!string.Equals(newBookPath, oldBookPath, StringComparison.Ordinal) && existing.Contains(newBookPath)) ||
                             !proposed.Add(newBookPath);
            if (collides)
            {
                error = CoreStrings.Get("Move_WouldDuplicate");
                return false;
            }

            newBookPaths.Add(newBookPath);
        }

        if (oldBookPaths.SequenceEqual(newBookPaths, StringComparer.Ordinal))
        {
            error = null;
            return true;
        }

        string group = MediaTypes.GetGroupFromMediaType(resources[0].MediaType, "other");
        List<string> groupFolders = _folderKeeper.GetFoldersForGroup(group).ToList();
        if (!groupFolders.Contains(normalizedFolder, StringComparer.Ordinal))
        {
            groupFolders.Add(normalizedFolder);
            _folderKeeper.SetFoldersForGroup(group, groupFolders);
        }

        _folderKeeper.BulkMoveResources(resources, newBookPaths);

        Dictionary<string, string> updates = new(StringComparer.Ordinal);
        List<string> notMoved = new();
        for (int i = 0; i < resources.Count; i++)
        {
            if (!string.Equals(resources[i].BookPath, newBookPaths[i], StringComparison.Ordinal))
            {
                notMoved.Add(oldBookPaths[i]);
                continue;
            }

            resources[i].CurrentBookRelPath = oldBookPaths[i];
            updates[oldBookPaths[i]] = newBookPaths[i];
        }

        if (updates.Count > 0)
        {
            UniversalUpdates.Perform(_book, updates);
            _book.Modified = true;
        }

        if (notMoved.Count > 0)
        {
            error = CoreStrings.Format("Move_Failed", string.Join(", ", notMoved));
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Validates a target folder path (path traversal / META-INF).</summary>
    private static bool IsFolderPathValid(string folderPath) =>
        !folderPath.Contains("..", StringComparison.Ordinal) &&
        !folderPath.Contains('\\') &&
        !folderPath.StartsWith('/') &&
        !folderPath.StartsWith('.') &&
        !folderPath.Contains("/.", StringComparison.Ordinal) &&
        !folderPath.StartsWith("META-INF", StringComparison.Ordinal) &&
        !folderPath.Contains("META-INF", StringComparison.Ordinal);

    /// <summary>
    /// Simulates the effect of <paramref name="updates"/> on every HTML file (without writing to the resource) and
    /// returns the bookpaths of those that were well-formed before the simulation but would stop being so after it.
    /// </summary>
    private List<string> SimulateAndFindNewlyMalformedHtml(Dictionary<string, string> updates)
    {
        Dictionary<string, string> cssUpdates = UniversalUpdates.SeparateStyleUpdates(updates);
        List<string> malformed = new();
        foreach (Resource resource in _folderKeeper.GetResourceList())
        {
            if (resource is not HtmlResource html)
            {
                continue;
            }

            string oldBookPath = html.BookPath;
            if (!WellFormedChecker.IsWellFormed(html.GetText(), html.MediaType))
            {
                continue;
            }

            string newBookPath = updates.TryGetValue(oldBookPath, out string? mapped) ? mapped : oldBookPath;
            string version = html.EpubVersion.Length > 0 ? html.EpubVersion : _book.EpubVersion;
            string simulated = PerformHtmlUpdates.Apply(html.GetText(), oldBookPath, newBookPath, updates, cssUpdates, version);
            if (!WellFormedChecker.IsWellFormed(simulated, html.MediaType))
            {
                malformed.Add(oldBookPath);
            }
        }

        return malformed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _folderKeeper.ResourceAdded -= OnFolderKeeperChanged;
        _folderKeeper.ResourceRemoved -= OnFolderKeeperChanged;
        foreach (Resource resource in _watchedResources)
        {
            resource.Renamed -= OnResourcePathChanged;
            resource.Moved -= OnResourcePathChanged;
        }

        _watchedResources.Clear();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyStringMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly OpfModelGroupKind[] GroupOrder =
    {
        OpfModelGroupKind.Text,
        OpfModelGroupKind.Styles,
        OpfModelGroupKind.Images,
        OpfModelGroupKind.Fonts,
        OpfModelGroupKind.Audio,
        OpfModelGroupKind.Video,
        OpfModelGroupKind.Misc,
    };

    private static int SpineRank(OpfModelEntry entry) =>
        entry.ReadingOrder < 0 ? int.MaxValue : entry.ReadingOrder;

    private static OpfModelGroupKind GroupFor(ResourceType type) => type switch
    {
        ResourceType.Html => OpfModelGroupKind.Text,
        ResourceType.Css => OpfModelGroupKind.Styles,
        ResourceType.Image or ResourceType.Svg => OpfModelGroupKind.Images,
        ResourceType.Font => OpfModelGroupKind.Fonts,
        ResourceType.Audio => OpfModelGroupKind.Audio,
        ResourceType.Video => OpfModelGroupKind.Video,
        _ => OpfModelGroupKind.Misc,
    };

    private static string GroupName(OpfModelGroupKind kind) => kind switch
    {
        OpfModelGroupKind.Text => "Text",
        OpfModelGroupKind.Styles => "Styles",
        OpfModelGroupKind.Images => "Images",
        OpfModelGroupKind.Fonts => "Fonts",
        OpfModelGroupKind.Audio => "Audio",
        OpfModelGroupKind.Video => "Video",
        _ => "Misc",
    };

    // Characters forbidden in file names.
    private static readonly char[] ForbiddenFilenameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static string ExtensionWithDot(string filename)
    {
        int dot = filename.LastIndexOf('.');
        return dot >= 0 ? filename[dot..] : string.Empty;
    }

    /// <summary>Whether a file name is valid (no dialogs — the message is returned through <paramref name="error"/>).</summary>
    private bool IsFilenameValid(string oldBookPath, string newFilename, out string? error)
    {
        foreach (char c in newFilename)
        {
            if (Array.IndexOf(ForbiddenFilenameChars, c) >= 0)
            {
                error = CoreStrings.Format("Rename_IllegalChar", c);
                return false;
            }
        }

        string extension = ExtensionWithDot(newFilename);
        string nameWithoutExtension = extension.Length > 0 ? newFilename[..^extension.Length] : newFilename;
        if (newFilename.Length == 0 || nameWithoutExtension.Length == 0)
        {
            error = CoreStrings.Get("Rename_Empty");
            return false;
        }

        string startDir = CoreBookPath.StartingDir(oldBookPath);
        string proposedBookPath = startDir.Length == 0 ? newFilename : startDir + "/" + newFilename;
        bool exists = _folderKeeper.GetAllBookPaths().Any(p =>
            !string.Equals(p, oldBookPath, StringComparison.Ordinal) &&
            string.Equals(p, proposedBookPath, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            error = CoreStrings.Format("Rename_InUse", newFilename);
            return false;
        }

        error = null;
        return true;
    }

    private static string SortKeyFor(string displayName)
    {
        int dot = displayName.LastIndexOf('.');
        return dot > 0 ? displayName[..dot] : displayName;
    }

    private static bool? CheckWellFormed(HtmlResource html)
    {
        try
        {
            return WellFormedChecker.IsWellFormed(html.GetText(), html.MediaType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string SafeCoverImagePath(OpfResource opf)
    {
        try
        {
            return opf.GetCoverImagePath();
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private Dictionary<string, IReadOnlyList<string>> BuildSemanticMap(OpfResource opf, bool epub3)
    {
        Dictionary<string, IReadOnlyList<string>> map = new(StringComparer.Ordinal);

        IReadOnlyDictionary<string, IReadOnlyList<string>> raw;
        if (epub3)
        {
            string navBookPath = opf.GetNavResourceBookPath();
            Resource? nav = navBookPath.Length > 0
                ? _folderKeeper.GetResourceByBookPathNoThrow(navBookPath)
                : null;
            raw = nav is HtmlResource navHtml
                ? new NavProcessor(navHtml).GetLandmarkNameForPaths()
                : EmptySemanticMap;
        }
        else
        {
            raw = opf.GetGuideSemanticNameForPaths();
        }

        foreach (KeyValuePair<string, IReadOnlyList<string>> pair in raw)
        {
            map[Utility.UrlDecodePath(pair.Key)] = pair.Value;
        }

        return map;
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptySemanticMap =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    private string DisplayNameFor(Resource resource)
    {
        if (_showFullPath)
        {
            return resource.BookPath;
        }

        return resource.ShortPathName.Length > 0
            ? resource.ShortPathName
            : System.IO.Path.GetFileName(resource.BookPath);
    }

    private static string BuildToolTip(OpfModelEntry entry)
    {
        string tip = entry.Resource.BookPath;
        if (entry.SemanticTypes.Count > 0)
        {
            tip += " (" + string.Join(",", entry.SemanticTypes) + ")";
        }

        if (entry.ManifestProperties.Length > 0)
        {
            tip += " [" + entry.ManifestProperties + "]";
        }

        return tip;
    }

    private void RewatchResources()
    {
        foreach (Resource resource in _watchedResources)
        {
            resource.Renamed -= OnResourcePathChanged;
            resource.Moved -= OnResourcePathChanged;
        }

        _watchedResources.Clear();
        foreach (Resource resource in _folderKeeper.GetResourceList())
        {
            resource.Renamed += OnResourcePathChanged;
            resource.Moved += OnResourcePathChanged;
            _watchedResources.Add(resource);
        }
    }

    private void OnFolderKeeperChanged(object? sender, ResourceEventArgs e)
    {
        if (!_suppressRefresh)
        {
            Refresh();
        }
    }

    private void OnResourcePathChanged(object? sender, ResourcePathChangedEventArgs e)
    {
        if (!_suppressRefresh)
        {
            Refresh();
        }
    }
}
