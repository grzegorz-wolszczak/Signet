using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Packs an unpacked EPUB tree (a directory from the corpus) into an OCF-compliant <c>.epub</c> archive:
/// the <c>mimetype</c> entry is first and uncompressed, the remaining entries are deflated.
/// The entry order is deterministic (ordinal), so two builds of the same tree
/// give an identical entry list.
/// </summary>
public static class EpubBuilder
{
    private const string MimetypeEntry = "mimetype";

    /// <summary>Builds an <c>.epub</c> at the given path (overwrites an existing file).</summary>
    public static void Build(string sourceDir, string outputEpubPath)
    {
        EnsureSourceExists(sourceDir);

        string? outDir = Path.GetDirectoryName(outputEpubPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using FileStream fs = File.Create(outputEpubPath);
        WriteArchive(fs, sourceDir, leaveOpen: false);
    }

    /// <summary>Builds an <c>.epub</c> in the temporary directory and returns the file path.</summary>
    /// <param name="sourceDir">The directory with the unpacked EPUB content.</param>
    /// <param name="temp">The temporary directory in which the file is created.</param>
    /// <param name="fileName">The file name; by default the source directory name + <c>.epub</c>.</param>
    public static string BuildInto(string sourceDir, TempDir temp, string? fileName = null)
    {
        string name = fileName ?? new DirectoryInfo(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name + ".epub";
        string path = temp.Combine(name);
        Build(sourceDir, path);
        return path;
    }

    /// <summary>Builds an <c>.epub</c> in memory and returns its bytes.</summary>
    public static byte[] BuildBytes(string sourceDir)
    {
        EnsureSourceExists(sourceDir);

        using MemoryStream ms = new();
        WriteArchive(ms, sourceDir, leaveOpen: true);
        return ms.ToArray();
    }

    private static void WriteArchive(Stream destination, string sourceDir, bool leaveOpen)
    {
        using ZipArchive zip = new(destination, ZipArchiveMode.Create, leaveOpen);

        foreach ((string entryName, string absolutePath) in EnumerateOrdered(sourceDir))
        {
            CompressionLevel level = entryName == MimetypeEntry
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;

            ZipArchiveEntry entry = zip.CreateEntry(entryName, level);
            using Stream target = entry.Open();
            using FileStream source = File.OpenRead(absolutePath);
            source.CopyTo(target);
        }
    }

    private static List<(string EntryName, string AbsolutePath)> EnumerateOrdered(string sourceDir)
    {
        string root = Path.GetFullPath(sourceDir);

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(abs => (
                EntryName: Path.GetRelativePath(root, abs).Replace(Path.DirectorySeparatorChar, '/'),
                AbsolutePath: abs))
            .OrderBy(f => f.EntryName == MimetypeEntry ? 0 : 1)
            .ThenBy(f => f.EntryName, StringComparer.Ordinal)
            .ToList();
    }

    private static void EnsureSourceExists(string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"The EPUB source directory does not exist: {sourceDir}");
        }
    }
}
