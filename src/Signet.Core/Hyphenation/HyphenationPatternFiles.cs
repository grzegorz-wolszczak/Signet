using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Signet.Core.Hyphenation;

/// <summary>An installed file of hyphenation patterns (<c>hyph_pl_PL.dic</c>) and the language it is for.</summary>
/// <param name="Language">The language tag of the file, from its name (<c>pl-PL</c>).</param>
/// <param name="Path">The full path of the file.</param>
public sealed record HyphenationPatternFile(string Language, string Path)
{
    /// <summary>The name of the file.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Which patterns a language of a book is hyphenated with.</summary>
/// <param name="Language">The language as the book states it (<c>pl</c>, <c>en-GB</c>).</param>
/// <param name="File">The patterns; <c>null</c> = none installed for the language.</param>
/// <param name="IsFallback">
/// Whether the patterns are of another variant of the language (the book says <c>en-GB</c>, only <c>en-US</c> is
/// installed): usable, but not exactly the hyphenation of the book's language — the user is warned.
/// </param>
/// <param name="ExpectedFileName">The file that would match the language exactly (<c>hyph_en_GB.dic</c>).</param>
public sealed record HyphenationMatch(string Language, HyphenationPatternFile? File, bool IsFallback, string ExpectedFileName)
{
    /// <summary>Whether the language can be hyphenated.</summary>
    public bool CanHyphenate => File is not null;
}

/// <summary>
/// The files of hyphenation patterns installed in a folder (the folder of the Hunspell dictionaries, where the
/// dictionary manager puts the files of a LibreOffice dictionary package) and the language each is for.
/// </summary>
/// <remarks>
/// A language is matched to a file by its tag: exactly (<c>pl-PL</c> → <c>hyph_pl_PL.dic</c>); a tag without a region
/// takes a file of the language (<c>pl</c> → <c>hyph_pl_PL.dic</c>); a tag whose region has no file takes a file of
/// another region of the language, as a fallback the user is told about (<c>en-GB</c> → <c>hyph_en_US.dic</c>). Of
/// several files of a language, the one whose region is the language itself (<c>de_DE</c>) is taken first, then the
/// first by name. Tags do not care about case and <c>_</c> is read as <c>-</c>.
/// </remarks>
public sealed class HyphenationPatternFiles
{
    /// <summary>The prefix of the name of a file of hyphenation patterns.</summary>
    public const string FilePrefix = "hyph_";

    /// <summary>The extension of a file of hyphenation patterns.</summary>
    public const string FileExtension = ".dic";

    // Patterns read once per file and version of it (they do not change while Signet runs, unless reinstalled).
    private static readonly ConcurrentDictionary<(string Path, DateTime Written), HyphenationPatterns> Loaded = new();

    /// <summary>Lists the files of hyphenation patterns in <paramref name="directory"/> (none when it does not exist).</summary>
    public HyphenationPatternFiles(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Installed = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, FilePrefix + "*" + FileExtension)
                .Select(path => new HyphenationPatternFile(LanguageOf(path), path))
                .Where(d => d.Language.Length > 0)
                .OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<HyphenationPatternFile>();
    }

    /// <summary>The installed files, by name.</summary>
    public IReadOnlyList<HyphenationPatternFile> Installed { get; }

    /// <summary>The patterns a language of a book is hyphenated with (see the remarks of the class).</summary>
    public HyphenationMatch Match(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        string tag = Normalize(language);
        string primary = PrimaryOf(tag);
        string expected = FilePrefix + (tag.Length == primary.Length ? primary + "_*" : tag.Replace('-', '_')) + FileExtension;
        if (primary.Length == 0)
        {
            return new HyphenationMatch(language, null, false, expected);
        }

        if (Installed.FirstOrDefault(d => string.Equals(d.Language, tag, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return new HyphenationMatch(language, exact, false, expected);
        }

        HyphenationPatternFile? ofLanguage = Installed
            .Where(d => string.Equals(PrimaryOf(d.Language), primary, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => string.Equals(d.Language, primary + "-" + primary, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        return new HyphenationMatch(language, ofLanguage, ofLanguage is not null && tag.Length > primary.Length, expected);
    }

    /// <summary>The patterns of an installed file, read once.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file holds no patterns.</exception>
    public static HyphenationPatterns Patterns(HyphenationPatternFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Loaded.GetOrAdd(
            (file.Path, System.IO.File.GetLastWriteTimeUtc(file.Path)),
            key => HyphenationPatterns.Load(key.Path));
    }

    /// <summary>A language tag as it is compared: <c>_</c> read as <c>-</c>, no spaces around it.</summary>
    public static string Normalize(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return language.Trim().Replace('_', '-');
    }

    // The language of "hyph_pl_PL.dic" is "pl-PL".
    private static string LanguageOf(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Length > FilePrefix.Length ? Normalize(name[FilePrefix.Length..]) : string.Empty;
    }

    private static string PrimaryOf(string tag)
    {
        int dash = tag.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? tag : tag[..dash];
    }
}
