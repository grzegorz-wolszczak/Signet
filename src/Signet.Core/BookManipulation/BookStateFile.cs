using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Signet.Core.Misc;
using Signet.Core.Resources;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>
/// A file holding book state that cannot be reconstructed from the working folder's files alone —
/// written next to them before the folder is frozen as a checkpoint
/// (<see cref="CheckpointHistory"/>) and read when the book is loaded again from that folder
/// (<see cref="ImportEpub.LoadWorkingFolder"/>).
/// </summary>
/// <remarks>
/// The only such information today is the font obfuscation algorithm
/// (<see cref="FontResource.ObfuscationAlgorithm"/>): fonts in the working folder are already
/// decrypted, and <c>META-INF/encryption.xml</c> in the working folder is stale (export
/// regenerates it from the resources). The file never ends up in the EPUB (it is skipped by
/// <see cref="ExportEpub"/>).
/// </remarks>
public static class BookStateFile
{
    /// <summary>The file name in the publication's root folder.</summary>
    public const string FileName = ".signet-state.xml";

    /// <summary>
    /// The name of the file in checkpoints made before the state was stored as XML. It is no longer read, but — like
    /// <see cref="FileName"/> — it is never part of the book.
    /// </summary>
    public const string LegacyFileName = ".signet-state.json";

    private const string RootElement = "signet_state";
    private const string FontElement = "font";
    private const string BookPathAttribute = "book_path";
    private const string ObfuscationAttribute = "obfuscation";

    /// <summary>Whether the book path (relative to the publication's root folder) is a state file, not a book file.</summary>
    public static bool IsStateFile(string bookPath) =>
        string.Equals(bookPath, FileName, StringComparison.Ordinal)
        || string.Equals(bookPath, LegacyFileName, StringComparison.Ordinal);

    /// <summary>
    /// Writes the state of <paramref name="book"/> to <see cref="FileName"/> in its working folder:
    /// <c>&lt;signet_state&gt;&lt;font book_path="…" obfuscation="…" /&gt;&lt;/signet_state&gt;</c>.
    /// </summary>
    public static void Write(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        XElement root = new(RootElement);
        foreach (FontResource font in book.GetFolderKeeper().GetResourceTypeList<FontResource>())
        {
            if (!string.IsNullOrEmpty(font.ObfuscationAlgorithm))
            {
                root.Add(new XElement(
                    FontElement,
                    new XAttribute(BookPathAttribute, font.BookPath),
                    new XAttribute(ObfuscationAttribute, font.ObfuscationAlgorithm)));
            }
        }

        XmlConfig.Save(SysPath.Combine(book.GetFolderKeeper().MainFolderPath, FileName), new XDocument(root));
    }

    /// <summary>
    /// Reads the font bookpath -&gt; obfuscation algorithm map from <see cref="FileName"/> in
    /// <paramref name="rootDirectory"/>. A missing or unreadable file yields an empty map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadFontObfuscation(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        Dictionary<string, string> fonts = new(StringComparer.Ordinal);
        if (XmlConfig.TryLoad(SysPath.Combine(rootDirectory, FileName))?.Root is not { } root || root.Name != RootElement)
        {
            return fonts;
        }

        foreach (XElement font in root.Elements(FontElement))
        {
            string bookPath = XmlConfig.Attribute(font, BookPathAttribute);
            string algorithm = XmlConfig.Attribute(font, ObfuscationAttribute);
            if (bookPath.Length > 0 && algorithm.Length > 0)
            {
                fonts[bookPath] = algorithm;
            }
        }

        return fonts;
    }
}
