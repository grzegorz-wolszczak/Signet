using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Localization;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Writes a <see cref="Book"/> model to an <c>.epub</c> file (OCF/ZIP), including the
/// <c>META-INF/encryption.xml</c> writer for obfuscated fonts.
/// </summary>
/// <remarks>
/// <para>Steps:
/// (1) when there are obfuscated fonts — ensure a UUID identifier in the OPF; (2) optionally stamp the OPF
/// with the editor version metadata and the modification date; (3) write the resource buffers to disk
/// (<see cref="Book.SaveAllResourcesToDisk"/>); (4) copy the working folder to a temporary folder;
/// (5) when there are obfuscated fonts — generate <c>META-INF/encryption.xml</c> and obfuscate the <em>copies</em>
/// of the font files (the originals in the working folder stay clean); (6) pack into a ZIP: <c>mimetype</c>
/// first and uncompressed, the rest deflated, paths with <c>/</c>, no directory entries.</para>
/// <para>ZIP entry dates/CRCs are not preserved between exports
/// (irrelevant for content comparisons); packing is sequential; failures are reported as
/// <see cref="EpubExportException"/>.</para>
/// </remarks>
public sealed class ExportEpub
{
    private const string MimetypeEntry = "mimetype";
    private const string EncryptionXmlBookPath = "META-INF/encryption.xml";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Book _book;

    /// <summary>Creates an exporter for the given book.</summary>
    /// <param name="book">The publication model to write.</param>
    public ExportEpub(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        _book = book;
    }

    /// <summary>
    /// Writes the book as an <c>.epub</c> to the given path (an existing file is overwritten).
    /// </summary>
    /// <param name="epubPath">The target path of the <c>.epub</c> file.</param>
    /// <param name="stampMetadata">
    /// When <c>true</c> (the default) — the <c>&lt;meta name="Signet version"&gt;</c> metadata and the
    /// modification date are added to the OPF. Round-trip tests can disable
    /// stamping to make the comparison fully deterministic.
    /// </param>
    /// <exception cref="EpubExportException">The output file could not be created or written.</exception>
    public void WriteBook(string epubPath, bool stampMetadata = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);

        OpfResource opf = _book.GetOpf();
        bool hasObfuscatedFonts = _book.HasObfuscatedFonts();

        // Font obfuscation requires a UUID identifier.
        if (hasObfuscatedFonts)
        {
            opf.EnsureUuidIdentifierPresent();
        }

        if (stampMetadata)
        {
            opf.AddSignetVersionMeta();
            opf.AddModificationDateMeta();
        }

        _book.SaveAllResourcesToDisk();

        using TempFolder staging = new();
        string stageRoot = staging.Path;

        CopyWorkingFolder(_book.GetFolderKeeper().MainFolderPath, stageRoot);

        // A fresh mimetype — OCF requires the exact content (without a trailing newline).
        File.WriteAllText(SysPath.Combine(stageRoot, MimetypeEntry), OcfReader.OcfMimetype, Utf8NoBom);

        if (hasObfuscatedFonts)
        {
            CreateEncryptionXml(stageRoot);
            ObfuscateFonts(stageRoot, opf);
        }

        WriteEpubArchive(stageRoot, epubPath);
    }

    // -----------------------------------------------------------------
    //  Stages
    // -----------------------------------------------------------------

    private static void CopyWorkingFolder(string sourceRoot, string destinationRoot)
    {
        foreach (string sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = SysPath.GetRelativePath(sourceRoot, sourceFile);
            string firstSegment = relative.Split(SysPath.DirectorySeparatorChar, SysPath.AltDirectorySeparatorChar)[0];

            // Neither the working folder's lock file nor the checkpoint state file may end up in the EPUB.
            if (string.Equals(firstSegment, TempFolder.LockFileName, StringComparison.Ordinal)
                || string.Equals(firstSegment, BookStateFile.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            string destinationFile = SysPath.Combine(destinationRoot, relative);
            Directory.CreateDirectory(SysPath.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private void CreateEncryptionXml(string stageRoot)
    {
        IReadOnlyList<FontResource> fonts = _book.GetFolderKeeper().GetResourceTypeList<FontResource>();
        string xml = EncryptionXmlWriter.WriteXml(fonts);

        string path = SysPath.Combine(stageRoot, EncryptionXmlBookPath.Replace('/', SysPath.DirectorySeparatorChar));
        Directory.CreateDirectory(SysPath.GetDirectoryName(path)!);
        File.WriteAllText(path, xml, Utf8NoBom);
    }

    private void ObfuscateFonts(string stageRoot, OpfResource opf)
    {
        string uuidId = opf.GetUuidIdentifierValue();
        string mainId = opf.GetMainIdentifierValue();

        foreach (FontResource font in _book.GetFolderKeeper().GetResourceTypeList<FontResource>())
        {
            string algorithm = font.ObfuscationAlgorithm;
            if (string.IsNullOrEmpty(algorithm))
            {
                continue;
            }

            string fontPath = SysPath.Combine(stageRoot, font.BookPath.Replace('/', SysPath.DirectorySeparatorChar));
            string identifier = string.Equals(algorithm, OcfReader.AdobeFontAlgorithmId, StringComparison.Ordinal)
                ? uuidId
                : mainId;

            FontObfuscation.ObfuscateFile(fontPath, algorithm, identifier);
        }
    }

    private static void WriteEpubArchive(string stageRoot, string epubPath)
    {
        string? outDir = SysPath.GetDirectoryName(SysPath.GetFullPath(epubPath));
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string tempFile = epubPath + ".tmp";

        try
        {
            using (FileStream fs = File.Create(tempFile))
            using (ZipArchive zip = new(fs, ZipArchiveMode.Create))
            {
                foreach ((string entryName, string absolutePath) in EnumerateOrdered(stageRoot))
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

            File.Move(tempFile, epubPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempFile);
            throw new EpubExportException(CoreStrings.Format("Error_SaveEpubFailed", epubPath), ex);
        }
    }

    private static List<(string EntryName, string AbsolutePath)> EnumerateOrdered(string stageRoot)
    {
        string root = SysPath.GetFullPath(stageRoot);

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(abs => (
                EntryName: SysPath.GetRelativePath(root, abs).Replace(SysPath.DirectorySeparatorChar, '/'),
                AbsolutePath: abs))
            .Where(f => f.EntryName != TempFolder.LockFileName)
            .OrderBy(f => f.EntryName == MimetypeEntry ? 0 : 1)
            .ThenBy(f => f.EntryName, StringComparer.Ordinal)
            .ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort
        }
    }
}
