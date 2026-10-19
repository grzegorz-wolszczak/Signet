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
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    throw new IOException(
                        $"Could not delete the temporary directory '{Path}' after {DeleteAttempts} attempts: a file in it "
                        + "is still open (a test did not dispose what holds it). Left: " + Left(), ex);
                }

                // A scanner or indexer can hold a fresh file for a moment.
                Thread.Sleep(DeleteRetryDelayMs);
            }
        }
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
