using System;
using System.IO;
using System.Threading;
using SysPath = System.IO.Path;

namespace Signet.Core.Resources;

/// <summary>
/// Represents a single book file on disk (in the working folder of the unpacked EPUB).
/// </summary>
/// <remarks>
/// Base class for all resource types. A plain <see cref="Resource"/> represents a
/// "miscellaneous" resource (<see cref="ResourceType.Generic"/>) — usually a binary file with no special handling.
/// <para>
/// The identifier is a ULID; changes are reported through .NET events; access is guarded by a
/// recursive <see cref="ReaderWriterLockSlim"/>; all operations are synchronous (no timer-delayed
/// updates); text files are always saved as UTF-8 without BOM with <c>\n</c> line endings.
/// </para>
/// </remarks>
public class Resource : IDisposable
{
    private string _fullFilePath;
    private string _currentBookRelPath = string.Empty;
    private bool _disposed;

    /// <summary>
    /// Creates a resource for the file <paramref name="fullFilePath"/> located in the
    /// <paramref name="mainFolder"/> tree (the main folder never ends with a separator).
    /// </summary>
    public Resource(string mainFolder, string fullFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(mainFolder);
        ArgumentException.ThrowIfNullOrEmpty(fullFilePath);
        MainFolder = mainFolder.TrimEnd(SysPath.DirectorySeparatorChar, SysPath.AltDirectorySeparatorChar);
        _fullFilePath = fullFilePath;
        Identifier = Ulid.NewUlid().ToString();
    }

    /// <summary>Event: the resource content was modified (in memory or on disk).</summary>
    public event EventHandler? Modified;

    /// <summary>Event: the resource was renamed. The argument carries the previous full path.</summary>
    public event EventHandler<ResourcePathChangedEventArgs>? Renamed;

    /// <summary>Event: the resource changed location (bookpath). The argument carries the previous full path.</summary>
    public event EventHandler<ResourcePathChangedEventArgs>? Moved;

    /// <summary>Event: the resource was deleted.</summary>
    public event EventHandler? Deleted;

    /// <summary>Event: the resource was saved / updated on disk by the application.</summary>
    public event EventHandler? ResourceUpdatedOnDisk;

    /// <summary>Event: the resource was refreshed from a newer version on disk (a change made outside the application).</summary>
    public event EventHandler? ResourceUpdatedFromDisk;

    /// <summary>Resource identifier (ULID). Stable for the whole lifetime of the object.</summary>
    public string Identifier { get; }

    /// <summary>Full path of the book's working folder (without a trailing separator).</summary>
    public string MainFolder { get; }

    /// <summary>Lock guarding access to the resource data (recursive).</summary>
    protected ReaderWriterLockSlim Lock { get; } = new(LockRecursionPolicy.SupportsRecursion);

    /// <summary>Full path of the file on disk.</summary>
    public string FullPath => _fullFilePath;

    /// <summary>Full path of the folder containing the file.</summary>
    public string FullFolderPath => SysPath.GetDirectoryName(_fullFilePath) ?? MainFolder;

    /// <summary>
    /// Path of the file inside the EPUB ("bookpath") — relative to <see cref="MainFolder"/>,
    /// with <c>/</c> as the separator and no leading <c>/</c>.
    /// </summary>
    public string BookPath
    {
        get
        {
            string full = _fullFilePath.Replace(SysPath.DirectorySeparatorChar, '/')
                .Replace(SysPath.AltDirectorySeparatorChar, '/');
            string root = MainFolder.Replace(SysPath.DirectorySeparatorChar, '/')
                .Replace(SysPath.AltDirectorySeparatorChar, '/');
            if (full.Length > root.Length + 1 &&
                full.StartsWith(root + "/", StringComparison.Ordinal))
            {
                return full[(root.Length + 1)..];
            }

            return full;
        }
    }

    /// <summary>File name (the last segment of <see cref="BookPath"/>).</summary>
    public string Filename
    {
        get
        {
            string bookPath = BookPath;
            int slash = bookPath.LastIndexOf('/');
            return slash < 0 ? bookPath : bookPath[(slash + 1)..];
        }
    }

    /// <summary>Bookpath of the folder containing the resource; <c>""</c> for the root.</summary>
    public string Folder => Core.BookPath.StartingDir(BookPath);

    /// <summary>Unique path suffix ending with the file name (Longest Common Path group). Set externally.</summary>
    public string ShortPathName { get; set; } = string.Empty;

    /// <summary>MIME type of the resource.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>EPUB version in which the resource is used (defaults to <c>2.0</c>).</summary>
    public string EpubVersion { get; set; } = "2.0";

    /// <summary>
    /// The resource's path in the imported EPUB. The getter returns <see cref="BookPath"/>
    /// when it has not been set.
    /// </summary>
    public string CurrentBookRelPath
    {
        get => string.IsNullOrEmpty(_currentBookRelPath) ? BookPath : _currentBookRelPath;
        set => _currentBookRelPath = value ?? string.Empty;
    }

    /// <summary>CRC-32 of the content from the last save (used on export).</summary>
    public string SavedCrc32 { get; set; } = string.Empty;

    /// <summary>Date of the last save (used on export).</summary>
    public string SavedDate { get; set; } = string.Empty;

    /// <summary>Size from the last save (used on export).</summary>
    public long SavedSize { get; set; }

    /// <summary>Resource type. Overridden by subclasses.</summary>
    public virtual ResourceType Type => ResourceType.Generic;

    /// <summary>
    /// Renames the file (within the same folder). Updates <see cref="ShortPathName"/>
    /// and raises <see cref="Renamed"/>. Returns <c>false</c> when the operation fails.
    /// </summary>
    public virtual bool RenameTo(string newFilename)
    {
        ArgumentException.ThrowIfNullOrEmpty(newFilename);
        string newPath;
        Lock.EnterWriteLock();
        try
        {
            newPath = SysPath.Combine(FullFolderPath, newFilename);
            if (!TryRenameOrMoveFile(_fullFilePath, newPath))
            {
                return false;
            }
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        string oldPath = _fullFilePath;
        _fullFilePath = newPath;
        ShortPathName = newFilename;
        Renamed?.Invoke(this, new ResourcePathChangedEventArgs(oldPath));
        return true;
    }

    /// <summary>
    /// Moves the file to a new bookpath (creating missing folders). Raises <see cref="Moved"/>.
    /// Returns <c>false</c> when the operation fails.
    /// </summary>
    public virtual bool MoveTo(string newBookPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(newBookPath);
        string newPath;
        Lock.EnterWriteLock();
        try
        {
            newPath = SysPath.Combine(MainFolder, newBookPath.Replace('/', SysPath.DirectorySeparatorChar));
            string? parent = SysPath.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!TryRenameOrMoveFile(_fullFilePath, newPath))
            {
                return false;
            }
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        string oldPath = _fullFilePath;
        _fullFilePath = newPath;
        Moved?.Invoke(this, new ResourcePathChangedEventArgs(oldPath));
        return true;
    }

    /// <summary>Deletes the file from disk and raises <see cref="Deleted"/>. Returns <c>false</c> on failure.</summary>
    public virtual bool Delete()
    {
        Lock.EnterWriteLock();
        try
        {
            if (!File.Exists(_fullFilePath))
            {
                return false;
            }

            File.Delete(_fullFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        Deleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Writes buffered data to disk. The default implementation does nothing (a binary resource
    /// does not keep its data in memory).
    /// </summary>
    /// <param name="bookWideSave">When <c>false</c>, <see cref="ResourceUpdatedOnDisk"/> is raised after the save.</param>
    public virtual void SaveToDisk(bool bookWideSave = false)
    {
    }

    /// <summary>Refreshes the resource data from the file on disk. Does nothing by default. Returns whether anything was loaded.</summary>
    protected virtual bool LoadFromDisk() => false;

    /// <summary>Raises the <see cref="Modified"/> event.</summary>
    protected void RaiseModified() => Modified?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises the <see cref="ResourceUpdatedOnDisk"/> event.</summary>
    protected void RaiseResourceUpdatedOnDisk() => ResourceUpdatedOnDisk?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises the <see cref="ResourceUpdatedFromDisk"/> event.</summary>
    protected void RaiseResourceUpdatedFromDisk() => ResourceUpdatedFromDisk?.Invoke(this, EventArgs.Empty);

    /// <summary>Releases resources (the lock). Does not remove the file from disk — use <see cref="Delete"/> for that.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources of subclasses.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Lock.Dispose();
        }

        _disposed = true;
    }

    private static bool TryRenameOrMoveFile(string oldPath, string newPath)
    {
        if (!File.Exists(oldPath))
        {
            return false;
        }

        bool samePath = string.Equals(
            SysPath.GetFullPath(oldPath), SysPath.GetFullPath(newPath), StringComparison.Ordinal);

        if (!samePath && File.Exists(newPath))
        {
            return false;
        }

        try
        {
            File.Move(oldPath, newPath, overwrite: samePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
