using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.EpubCheck.Core.Ocf;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The book as EPUBCheck sees it (<see cref="IEpubSource"/>): the files the book would be saved with, including the changes
/// that are only in memory.
/// </summary>
/// <remarks>
/// <para>It follows <see cref="ExportEpub"/> without writing anything: the files of the working folder, except the lock file and
/// the checkpoint state file; the text resources as they would be written (the in-memory text, normalized and in UTF-8 without a
/// BOM); a fresh <c>mimetype</c> first; and, when the book has obfuscated fonts, <c>META-INF/encryption.xml</c> and the obfuscated
/// fonts. Unlike the export it does not stamp the OPF and does not add a missing UUID identifier to it: validating must not change
/// the book.</para>
/// <para>The source is a snapshot of the names and sizes taken when it is created. The text that was in memory and the obfuscated
/// fonts are held as bytes; every other file is read from the working folder when it is opened. It is not packaged
/// (<see cref="IsPackaged"/> is <c>false</c>), so the sizes are those of the files and no compression is claimed beyond
/// <c>mimetype</c> being stored.</para>
/// </remarks>
public sealed class BookEpubSource : IEpubSource
{
    private const string MimetypeEntry = "mimetype";
    private const string EncryptionXmlEntry = "META-INF/encryption.xml";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<Stream>[] _openers;

    private BookEpubSource(EpubSourceEntry[] entries, Func<Stream>[] openers)
    {
        Entries = entries;
        _openers = openers;
    }

    /// <inheritdoc />
    public bool IsPackaged => false;

    /// <inheritdoc />
    public IReadOnlyList<EpubSourceEntry> Entries { get; }

    /// <summary>Takes the files of the book as they are now (unsaved text included).</summary>
    public static BookEpubSource Create(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        string root = folderKeeper.MainFolderPath;
        Dictionary<string, Func<Stream>> files = new(StringComparer.Ordinal);
        Dictionary<string, long> lengths = new(StringComparer.Ordinal);

        // The files on disk first (the working folder is what the export copies) ...
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = SysPath.GetRelativePath(root, file).Replace(SysPath.DirectorySeparatorChar, '/');
            if (IsOutsideThePublication(name))
            {
                continue;
            }

            string path = file;
            files[name] = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            lengths[name] = new FileInfo(file).Length;
        }

        // ... then what the book holds in memory instead of (or without) its file.
        foreach (TextResource text in folderKeeper.GetResourceList().OfType<TextResource>().Where(t => t.IsLoaded))
        {
            byte[] bytes = Utf8NoBom.GetBytes(Utility.ConvertLineEndingsAndNormalize(text.GetText()));
            AddBytes(files, lengths, text.BookPath, bytes);
        }

        AddBytes(files, lengths, MimetypeEntry, Utf8NoBom.GetBytes(OcfReader.OcfMimetype));
        if (book.HasObfuscatedFonts())
        {
            AddEncryptionAndObfuscatedFonts(book, files, lengths, root);
        }

        string[] names = files.Keys
            .OrderBy(n => n == MimetypeEntry ? 0 : 1)
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToArray();
        EpubSourceEntry[] entries = names.Select((name, order) => new EpubSourceEntry(
            name,
            order,
            name == MimetypeEntry ? CompressionMethod.Stored : CompressionMethod.Deflate,
            lengths[name],
            lengths[name],
            IsDirectory: false)).ToArray();
        return new BookEpubSource(entries, names.Select(n => files[n]).ToArray());
    }

    /// <inheritdoc />
    public Stream OpenRead(EpubSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if ((uint)entry.Order >= (uint)Entries.Count || !Entries[entry.Order].Equals(entry))
        {
            throw new ArgumentException("The entry does not belong to this source.", nameof(entry));
        }

        return _openers[entry.Order]();
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    // Neither the working folder's lock file nor the checkpoint state file ends up in the EPUB (the export skips them by their
    // first path segment).
    private static bool IsOutsideThePublication(string name)
    {
        int slash = name.IndexOf('/', StringComparison.Ordinal);
        string firstSegment = slash < 0 ? name : name[..slash];
        return string.Equals(firstSegment, TempFolder.LockFileName, StringComparison.Ordinal) || BookStateFile.IsStateFile(firstSegment);
    }

    private static void AddBytes(Dictionary<string, Func<Stream>> files, Dictionary<string, long> lengths, string name, byte[] bytes)
    {
        files[name] = () => new MemoryStream(bytes, writable: false);
        lengths[name] = bytes.Length;
    }

    // The export writes META-INF/encryption.xml and obfuscates copies of the fonts (the originals stay clean).
    private static void AddEncryptionAndObfuscatedFonts(Book book, Dictionary<string, Func<Stream>> files, Dictionary<string, long> lengths, string root)
    {
        IReadOnlyList<FontResource> fonts = book.GetFolderKeeper().GetResourceTypeList<FontResource>();
        AddBytes(files, lengths, EncryptionXmlEntry, Utf8NoBom.GetBytes(EncryptionXmlWriter.WriteXml(fonts)));

        OpfResource opf = book.GetOpf();
        string uuidId = opf.GetUuidIdentifierValue();
        string mainId = opf.GetMainIdentifierValue();
        foreach (FontResource font in fonts)
        {
            string algorithm = font.ObfuscationAlgorithm;
            string fontPath = SysPath.Combine(root, font.BookPath.Replace('/', SysPath.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(algorithm) || !File.Exists(fontPath))
            {
                continue;
            }

            string identifier = string.Equals(algorithm, OcfReader.AdobeFontAlgorithmId, StringComparison.Ordinal) ? uuidId : mainId;
            if (string.IsNullOrEmpty(identifier))
            {
                // The export adds a UUID identifier to the OPF first; the book is not changed here, so the font stays as it is.
                continue;
            }

            byte[] bytes = File.ReadAllBytes(fontPath);
            try
            {
                FontObfuscation.ObfuscateBytes(bytes, algorithm, identifier, font.BookPath);
            }
            catch (FontObfuscationException)
            {
                continue;
            }

            AddBytes(files, lengths, font.BookPath, bytes);
        }
    }
}
