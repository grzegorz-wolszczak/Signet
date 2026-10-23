using System;
using System.IO;
using System.Linq;
using System.Threading;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// A disposable temporary directory under <c>%TEMP%/Signet.Tests</c>, deleted recursively
/// on <see cref="Dispose"/>. A few retries cover a short-lived lock; when a file stays open (a test did not
/// dispose what holds it) <see cref="Dispose"/> throws, naming the directory and the files left.
/// </summary>
public sealed class TempDir : IDisposable
{
    private const int DeleteAttempts = 5;
    private const int DeleteRetryDelayMs = 100;

    /// <summary>Creates a new, unique temporary directory.</summary>
    /// <param name="prefix">An optional directory name prefix.</param>
    public TempDir(string? prefix = null)
    {
        string name = (prefix ?? "Signet-") + Guid.NewGuid().ToString("N");
        Path = SysPath.Combine(SysPath.GetTempPath(), "Signet.Tests", name);
        Directory.CreateDirectory(Path);
    }

    /// <summary>The full path of the directory.</summary>
    public string Path { get; }

    /// <summary>Builds a path to a file or subdirectory inside this directory.</summary>
    public string Combine(params string[] parts)
    {
        string[] all = new string[parts.Length + 1];
        all[0] = Path;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return SysPath.Combine(all);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        for (int attempt = 1; ; attempt++)
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            // Deleting the directory alone would catch a file another handle still holds open on Windows (the OS
            // refuses), but not on Linux/WSL: removing a directory entry there does not check for open handles on
            // it, the inode survives until the last one closes. Probing each file for an exclusive open catches it
            // on both — see FindOpenFile.
            string? locked = FindOpenFile();
            Exception failure;
            if (locked is null)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failure = ex;
                }
            }
            else
            {
                failure = new IOException($"'{locked}' is still open.");
            }

            if (attempt == DeleteAttempts)
            {
                throw new IOException(
                    $"Could not delete the temporary directory '{Path}' after {DeleteAttempts} attempts: a file in it "
                    + "is still open (a test did not dispose what holds it). Left: " + Left(), failure);
            }

            // A scanner or indexer can hold a fresh file for a moment.
            Thread.Sleep(DeleteRetryDelayMs);
        }
    }

    // Requesting FileShare.None here conflicts with any existing open handle regardless of its own access/share
    // mode (on both Windows and Linux/WSL — confirmed: .NET enforces FileShare in-process even on Unix, only the
    // delete syscall does not), and FileAccess.Read keeps the probe itself from failing on a merely read-only file.
    private string? FindOpenFile()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    using FileStream probe = new(file, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return file;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The directory may already be gone between the Directory.Exists check and here.
        }

        return null;
    }

    private string Left()
    {
        try
        {
            return string.Join(", ", Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                .Select(file => SysPath.GetRelativePath(Path, file)).Take(10));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(unreadable)";
        }
    }
}
