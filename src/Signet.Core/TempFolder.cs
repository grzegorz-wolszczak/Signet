using System;
using System.IO;
using System.Linq;
using System.Threading;
using SysPath = System.IO.Path;

namespace Signet.Core;

/// <summary>
/// A handle (RAII / <see cref="IDisposable"/>) to a working directory on disk. Creating the
/// object creates a unique directory; <see cref="Dispose"/> deletes it recursively along with
/// its contents.
/// </summary>
/// <remarks>
/// Every open book (<c>FolderKeeper</c>) and the import/export operations unpack the EPUB into such a
/// directory and work on the files, and the directory is deleted when the book is closed.
/// <para>
/// Deletion is <b>synchronous</b>, with a short retry loop for temporarily locked files
/// (on Windows <see cref="Directory.Delete(string, bool)"/> throws where a lenient recursive
/// removal would not). The directory name comes from <see cref="Ulid"/> (see CLAUDE.md), not from a
/// <c>Signet-XXXXXX</c> template.
/// </para>
/// <para>
/// <b>Cleanup after a crash</b>: every instance keeps a lock file (<see cref="LockFileName"/>) in its
/// directory, opened exclusively for its whole lifetime. When the process crashes, the system releases the handle.
/// <see cref="CleanOrphanedScratchpad"/> (called at application startup)
/// deletes those <c>Signet-*</c> directories whose lock can be taken over, and skips those held by live instances.
/// </para>
/// </remarks>
public sealed class TempFolder : IDisposable
{
    /// <summary>The name of the lock file created in every working directory.</summary>
    public const string LockFileName = ".signet-lock";

    private const string FolderPrefix = "Signet-";
    private const int DeleteRetryCount = 3;
    private const int DeleteRetryDelayMilliseconds = 50;

    // A directory without a lock file but younger than this is treated as just being created
    // by a live instance (the window between creating the directory and creating the lock) and is left alone.
    private static readonly TimeSpan LocklessGracePeriod = TimeSpan.FromSeconds(30);

    private static string s_scratchpadRoot =
        SysPath.Combine(SysPath.GetTempPath(), ApplicationInfo.Name, "scratch");

    private FileStream? _lock;
    private bool _disposed;

    /// <summary>
    /// Creates a new working directory under the shared "scratchpad" directory
    /// (<see cref="ScratchpadRoot"/>).
    /// </summary>
    public TempFolder()
        : this(ScratchpadRoot)
    {
    }

    /// <summary>
    /// Creates a new working directory directly under <paramref name="baseDirectory"/>.
    /// </summary>
    /// <param name="baseDirectory">The parent directory; it is created if it does not exist.</param>
    public TempFolder(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string candidate = SysPath.Combine(baseDirectory, FolderPrefix + Ulid.NewUlid().ToString());
        Directory.CreateDirectory(candidate);

        // The path without a trailing separator.
        Path = candidate.TrimEnd(SysPath.DirectorySeparatorChar, SysPath.AltDirectorySeparatorChar);

        // The lock file is created right away — held open exclusively until Dispose.
        _lock = new FileStream(
            SysPath.Combine(Path, LockFileName),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);
    }

    /// <summary>
    /// The shared parent directory in which working directories are created.
    /// <c>&lt;temp&gt;/Signet/scratch</c> by default. A hook for a future
    /// <c>SettingsStore</c> - the application can override it at startup.
    /// </summary>
    public static string ScratchpadRoot
    {
        get => s_scratchpadRoot;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            s_scratchpadRoot = value;
        }
    }

    /// <summary>
    /// The full path of the working directory, without a trailing separator.
    /// It remains available after <see cref="Dispose"/>
    /// (the directory no longer exists by then).
    /// </summary>
    public string Path { get; }

    /// <summary>Whether <see cref="Dispose"/> has already been called.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Ensures the <see cref="ScratchpadRoot"/> directory exists and returns its path.
    /// </summary>
    public static string GetPathToScratchpad()
    {
        Directory.CreateDirectory(ScratchpadRoot);
        return ScratchpadRoot;
    }

    /// <summary>
    /// Deletes orphaned working directories under <see cref="ScratchpadRoot"/> — left over after a
    /// crash of the previous session. Directories held by live instances (including in another
    /// process) are skipped. Intended to be called once, at application startup.
    /// </summary>
    /// <returns>The number of removed directories.</returns>
    public static int CleanOrphanedScratchpad() => CleanOrphaned(ScratchpadRoot);

    /// <summary>
    /// Like <see cref="CleanOrphanedScratchpad"/>, but for any parent directory.
    /// </summary>
    /// <param name="baseDirectory">The directory in which to look for <c>Signet-*</c> directories.</param>
    /// <returns>The number of removed directories.</returns>
    public static int CleanOrphaned(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        if (!Directory.Exists(baseDirectory))
        {
            return 0;
        }

        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(baseDirectory, FolderPrefix + "*"))
        {
            if (IsInUse(directory))
            {
                continue;
            }

            if (TryDeleteRecursively(directory))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Deletes the working directory along with all its contents. Safe to call multiple times.
    /// Makes several attempts for temporarily locked files; if it still
    /// fails, it ends silently (best-effort).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _lock?.Dispose();
        }
        catch (IOException)
        {
            // best-effort
        }
        finally
        {
            _lock = null;
        }

        TryDeleteRecursively(Path);
    }

    /// <summary>
    /// Recursively deletes a directory with retries. Returns <see langword="true"/>
    /// if the directory does not exist at the end.
    /// </summary>
    internal static bool TryDeleteRecursively(string directory)
    {
        for (int attempt = 1; attempt <= DeleteRetryCount; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteRetryCount)
                {
                    return !Directory.Exists(directory);
                }

                ClearReadOnlyAttributes(directory);
                Thread.Sleep(DeleteRetryDelayMilliseconds);
            }
        }

        return !Directory.Exists(directory);
    }

    /// <summary>
    /// Whether the given working directory is in use by a live instance (also in another process).
    /// </summary>
    private static bool IsInUse(string directory)
    {
        string lockPath = SysPath.Combine(directory, LockFileName);

        if (File.Exists(lockPath))
        {
            try
            {
                using FileStream _ = new(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false; // the lock was taken over — nobody holds it
            }
            catch (IOException)
            {
                return true; // held by a live instance
            }
            catch (UnauthorizedAccessException)
            {
                return true; // unknown — conservatively left alone
            }
        }

        // No lock file: either a directory from before this feature / a damaged one (we delete it),
        // or one just being created by a live instance (protected by a short grace period).
        try
        {
            DateTime newest = new[]
            {
                Directory.GetCreationTimeUtc(directory),
                Directory.GetLastWriteTimeUtc(directory),
            }.Max();

            return DateTime.UtcNow - newest < LocklessGracePeriod;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        try
        {
            DirectoryInfo root = new(directory);
            if (!root.Exists)
            {
                return;
            }

            root.Attributes = FileAttributes.Directory;
            foreach (FileSystemInfo entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort - if the attributes cannot be cleared, the next delete attempt will try anyway
        }
    }
}
