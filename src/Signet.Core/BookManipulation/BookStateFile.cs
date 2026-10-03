using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
    public const string FileName = ".signet-state.json";

    /// <summary>
    /// Writes the state of <paramref name="book"/> to <see cref="FileName"/> in its working folder.
    /// </summary>
    public static void Write(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        Dictionary<string, string> fonts = new(StringComparer.Ordinal);
        foreach (FontResource font in book.GetFolderKeeper().GetResourceTypeList<FontResource>())
        {
            if (!string.IsNullOrEmpty(font.ObfuscationAlgorithm))
            {
                fonts[font.BookPath] = font.ObfuscationAlgorithm;
            }
        }

        State state = new() { FontObfuscation = fonts };
        string path = SysPath.Combine(book.GetFolderKeeper().MainFolderPath, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }

    /// <summary>
    /// Reads the font bookpath -&gt; obfuscation algorithm map from <see cref="FileName"/> in
    /// <paramref name="rootDirectory"/>. A missing or unreadable file yields an empty map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadFontObfuscation(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        string path = SysPath.Combine(rootDirectory, FileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            State? state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            return state?.FontObfuscation is { } fonts
                ? new Dictionary<string, string>(fonts, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private sealed class State
    {
        public Dictionary<string, string>? FontObfuscation { get; set; }
    }
}
