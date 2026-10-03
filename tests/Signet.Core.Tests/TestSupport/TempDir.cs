using System;
using System.IO;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// A disposable temporary directory under <c>%TEMP%/Signet.Tests</c>, deleted recursively
/// on <see cref="Dispose"/> (best-effort).
/// </summary>
public sealed class TempDir : IDisposable
{
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
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup — e.g. a temporarily locked file.
        }
        catch (UnauthorizedAccessException)
        {
            // j.w.
        }
    }
}
