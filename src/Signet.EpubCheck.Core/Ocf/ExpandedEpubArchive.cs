using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Packs an expanded EPUB (a folder) into an EPUB file so that it can be checked as a packaged one (upstream's <c>Archive</c>):
/// the <c>mimetype</c> file goes first and stored, the other files follow compressed.
/// </summary>
public sealed class ExpandedEpubArchive : IDisposable
{
    private const string MimeTypeEntry = "mimetype";
    private const string EpubExtension = ".epub";

    private readonly bool _keep;

    private ExpandedEpubArchive(string epubPath, bool keep)
    {
        EpubPath = epubPath;
        _keep = keep;
    }

    /// <summary>The EPUB file made from the folder.</summary>
    public string EpubPath { get; }

    /// <summary>
    /// Makes the EPUB file of the folder, next to it as <c>&lt;folder&gt;.epub</c> as upstream does. With <paramref name="keep"/> the
    /// file stays; otherwise <see cref="Dispose"/> deletes it. Without <paramref name="keep"/> a file of that name that already
    /// exists is not touched (the archive goes to a temporary file instead), which upstream would overwrite and delete.
    /// </summary>
    public static ExpandedEpubArchive Create(string folder, bool keep, string? outputPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string target = outputPath is null ? root + EpubExtension : Path.GetFullPath(outputPath);
        if (outputPath is null && !keep && File.Exists(target))
        {
            target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + EpubExtension);
        }

        ExpandedEpubArchive archive = new(target, keep);
        try
        {
            Write(root, target);
        }
        catch
        {
            archive.Dispose();
            throw;
        }

        return archive;
    }

    /// <summary>Deletes the EPUB file unless it is kept.</summary>
    public void Dispose()
    {
        if (!_keep)
        {
            try
            {
                File.Delete(EpubPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be deleted is left to the system's cleaning of the temporary folder.
            }
        }
    }

    private static void Write(string root, string target)
    {
        using FileStream stream = new(target, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

        string mimetype = Path.Combine(root, MimeTypeEntry);
        if (File.Exists(mimetype))
        {
            AddFile(zip, mimetype, MimeTypeEntry, CompressionLevel.NoCompression);
        }

        List<string> files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(name => name != MimeTypeEntry)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (string name in files)
        {
            AddFile(zip, Path.Combine(root, name), name, CompressionLevel.Optimal);
        }
    }

    private static void AddFile(ZipArchive zip, string file, string entryName, CompressionLevel level)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, level);
        entry.LastWriteTime = File.GetLastWriteTime(file);
        using Stream output = entry.Open();
        using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        input.CopyTo(output);
    }
}
