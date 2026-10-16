using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Signet.Core.Misc;

/// <summary>
/// Tells the instances of the application apart that have the same book file open: an instance that opens a file takes
/// a <see cref="BookFileLock"/> on it, and another instance that tries to take it finds the file in use.
/// </summary>
/// <remarks>
/// A lock is an exclusive (<see cref="FileShare.None"/>) file held open in a folder of the application, named after a hash
/// of the normalized path of the book — nothing appears next to the book. The system releases the lock when the instance
/// ends, however it ends, so a crashed instance leaves no lock behind. The lock is advisory: it only warns the instances
/// that ask; it does not stop another program from opening the file.
/// </remarks>
public sealed class BookFileLocks
{
    private readonly string _directory;

    /// <summary>Creates the locks in <paramref name="directory"/> (created when the first lock is taken).</summary>
    public BookFileLocks(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    /// <summary>
    /// The locks in the <c>locks</c> folder next to the settings file (so, in the application's preferences folder; a test
    /// with its own settings file has its own locks).
    /// </summary>
    public static BookFileLocks BesideSettings(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new BookFileLocks(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.FilePath))!, "locks"));
    }

    /// <summary>
    /// Takes the lock of the book file at <paramref name="path"/>.
    /// </summary>
    /// <returns>The lock, or <c>null</c> when another instance (or this one, through another lock) holds it already, or
    /// the lock cannot be created at all (a read-only folder): the file is then treated as free — a lock that cannot be
    /// taken must not stop a book from being opened.</returns>
    public BookFileLock? TryAcquire(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string key = KeyOf(path);
        string lockPath = Path.Combine(_directory, Hash(key) + ".lock");
        try
        {
            Directory.CreateDirectory(_directory);
            FileStream stream = new(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return new BookFileLock(key, stream);
        }
        catch (IOException) when (File.Exists(lockPath))
        {
            // The file exists and cannot be opened exclusively: somebody holds it.
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BookFileLock(key, null);
        }
    }

    /// <summary>
    /// The identity of a book file: its full path, in the case the file system ignores (Windows, macOS) folded — so
    /// <c>C:\Books\A.epub</c> and <c>c:\books\a.EPUB</c> are the same book.
    /// </summary>
    public static string KeyOf(string path)
    {
        string full = Path.GetFullPath(path);
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? full : full.ToUpperInvariant();
    }

    private static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16);
}

/// <summary>A lock on a book file taken from <see cref="BookFileLocks"/>; disposing it releases the file.</summary>
public sealed class BookFileLock : IDisposable
{
    private readonly FileStream? _stream;

    internal BookFileLock(string key, FileStream? stream)
    {
        Key = key;
        _stream = stream;
    }

    /// <summary>The identity of the locked book file (<see cref="BookFileLocks.KeyOf"/>).</summary>
    public string Key { get; }

    /// <summary>Releases the lock.</summary>
    public void Dispose() => _stream?.Dispose();
}
