using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Signet.Core.Localization;
using WeCantSpell.Hunspell;

namespace Signet.Core.Spellcheck;

/// <summary>What a file of a Hunspell dictionary package is.</summary>
public enum DictionaryFileRole
{
    /// <summary>The <c>.aff</c>/<c>.dic</c> pair — the only files Signet reads.</summary>
    Required,

    /// <summary>libhyphen hyphenation patterns (<c>hyph_*.dic</c>) and their TeX source.</summary>
    Hyphenation,

    /// <summary>A MyThes thesaurus (<c>th_*_v2.dat</c>/<c>.idx</c>).</summary>
    Thesaurus,

    /// <summary>LibreOffice's registration file of the package (<c>dictionaries.xcu</c>).</summary>
    LibreOffice,

    /// <summary>A readme or a licence.</summary>
    Documentation,
}

/// <summary>A single file of an installed dictionary.</summary>
/// <param name="Path">Its full path.</param>
/// <param name="Role">What the file is.</param>
public readonly record struct DictionaryFile(string Path, DictionaryFileRole Role);

/// <summary>A dictionary available to the spell checker, as the dictionary manager sees it.</summary>
/// <param name="Name">The dictionary name (the base name of its files, e.g. <c>pl_PL</c>).</param>
/// <param name="Files">Its files on disk (empty for the built-in one).</param>
/// <param name="IsBuiltIn">Whether this is the built-in <c>en_US</c> (it has no files and cannot be deleted).</param>
/// <param name="OverridesBuiltIn">Whether this installed dictionary shadows the built-in one of the same name.</param>
/// <param name="Problem">Why the dictionary cannot be used; <see langword="null"/> when it works.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "A spelling dictionary is what the type is — \"Dictionary\" is this domain's established name "
        + "(Resource, FolderKeeper, OpfResource…), not the collection suffix the rule warns about.")]
public sealed record InstalledDictionary(
    string Name,
    IReadOnlyList<DictionaryFile> Files,
    bool IsBuiltIn,
    bool OverridesBuiltIn,
    string? Problem);

/// <summary>The outcome of validating or installing a set of files chosen by the user.</summary>
/// <param name="Installed">Whether the dictionary was installed (always <see langword="false"/> for a validation).</param>
/// <param name="Name">The name the dictionary has (or would have); <see langword="null"/> when it could not be told.</param>
/// <param name="RefusedFiles">The chosen files that are not files of a dictionary package (by name).</param>
/// <param name="Problem">Why the operation failed; <see langword="null"/> when nothing was wrong.</param>
public sealed record DictionaryInstallResult(
    bool Installed,
    string? Name,
    IReadOnlyList<string> RefusedFiles,
    string? Problem)
{
    /// <summary>
    /// Whether the only thing wrong was that a dictionary of this name is already installed — the caller may ask
    /// the user and install again with <c>replace: true</c>.
    /// </summary>
    public bool NameTaken { get; init; }

    /// <summary>Whether nothing was wrong.</summary>
    public bool IsOk => Problem is null && RefusedFiles.Count == 0;
}

/// <summary>
/// Installs, lists and removes Hunspell dictionaries in the folder the spell checker reads
/// (<c>AppDirectories.HunspellDictionariesDirectory</c>) — the engine behind the dictionary manager window.
/// No UI, so it can be tested without Avalonia.
/// </summary>
/// <remarks>
/// <para>A dictionary is the pair <c>&lt;name&gt;.aff</c> + <c>&lt;name&gt;.dic</c>; that is all
/// <see cref="SpellChecker"/> reads. The companion files of a LibreOffice dictionary package (hyphenation patterns,
/// a thesaurus, <c>dictionaries.xcu</c>, readmes and licences) may be chosen along with the pair and are then kept
/// next to it — Signet never reads them, but deleting a dictionary takes them with it.</para>
/// <para>A candidate is checked in three steps — by name, by shape, by content (the pair is actually loaded before
/// anything is copied) — and the first failure stops the install, so a refused add leaves both the source files and
/// the target folder exactly as they were.</para>
/// </remarks>
public static class HunspellDictionaryInstaller
{
    // The extensions a dictionary package may hold. A ".dic_delta" (Sigil's delta files) is not among them
    // deliberately — SpellChecker does not read them.
    private static readonly string[] AllowedExtensions =
    {
        ".aff", ".dic", ".dat", ".idx", ".xcu", ".tex", ".txt",
    };

    // A file without an extension is accepted when it is a licence or a readme of the package.
    private static readonly string[] DocumentationNames = { "readme", "license", "licence", "copying", "changelog" };

    // The directives a real .aff file starts a line with; at least one must be there.
    private static readonly string[] AffixDirectives =
    {
        "SET", "TRY", "FLAG", "WORDCHARS", "SFX", "PFX", "REP", "MAP", "KEEPCASE", "NOSUGGEST",
        "ICONV", "OCONV", "COMPOUND", "NEEDAFFIX", "ONLYINCOMPOUND", "BREAK", "LANG", "IGNORE",
    };

    // The prefix of the temporary folder an install copies into before moving the files in.
    private const string StagingPrefix = ".signet-install-";

    /// <summary>
    /// The dictionaries available to the spell checker: the built-in <c>en_US</c> first, then the ones installed in
    /// <paramref name="directory"/>, sorted by name. An installed dictionary with the name of the built-in one
    /// replaces it in the list and says so (<see cref="InstalledDictionary.OverridesBuiltIn"/>); a pair put there by
    /// hand that cannot be used is listed too, with its <see cref="InstalledDictionary.Problem"/>.
    /// </summary>
    public static IReadOnlyList<InstalledDictionary> List(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        List<InstalledDictionary> result = new();
        IReadOnlyList<string> names = InstalledNames(directory);
        if (!names.Any(n => string.Equals(n, SpellChecker.BuiltInDictionaryName, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new InstalledDictionary(
                SpellChecker.BuiltInDictionaryName, Array.Empty<DictionaryFile>(), IsBuiltIn: true, OverridesBuiltIn: false, Problem: null));
        }

        foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new InstalledDictionary(
                name,
                FilesOf(directory, name).Select(path => new DictionaryFile(path, RoleOf(path))).ToList(),
                IsBuiltIn: false,
                OverridesBuiltIn: string.Equals(name, SpellChecker.BuiltInDictionaryName, StringComparison.OrdinalIgnoreCase),
                Problem: ProblemOf(directory, name)));
        }

        return result;
    }

    /// <summary>
    /// Checks the files the user chose — by name, by shape and by content — without copying anything.
    /// </summary>
    public static DictionaryInstallResult Validate(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        List<string> refused = files
            .Where(path => !IsPackageFile(path))
            .Select(path => Path.GetFileName(path))
            .ToList();
        if (refused.Count > 0)
        {
            return new DictionaryInstallResult(
                Installed: false, Name: null, refused, CoreStrings.Format("DictionaryInstall_NotDictionaryFiles", Join(refused)));
        }

        string[] affixes = files.Where(IsAffixFile).ToArray();
        if (affixes.Length == 0)
        {
            return Failed(null, CoreStrings.Get("DictionaryInstall_MissingAff"));
        }

        if (affixes.Length > 1)
        {
            return Failed(
                null,
                CoreStrings.Format("DictionaryInstall_SeveralAff", Join(affixes.Select(Path.GetFileName).ToList())));
        }

        string affPath = affixes[0];
        string name = Path.GetFileNameWithoutExtension(affPath);
        string? dicPath = files.FirstOrDefault(path =>
            IsWordListFile(path) && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));
        if (dicPath is null)
        {
            return Failed(name, CoreStrings.Format("DictionaryInstall_MissingDic", name + ".dic"));
        }

        string? content = CheckContent(affPath, dicPath);
        return content is null
            ? new DictionaryInstallResult(Installed: false, name, Array.Empty<string>(), Problem: null)
            : Failed(name, content);
    }

    /// <summary>
    /// Installs the chosen files into <paramref name="directory"/>: validates them
    /// (<see cref="Validate"/>), copies them into a temporary folder inside the target directory and moves them in
    /// only when everything passed — a failed install leaves the folder exactly as it was. With
    /// <paramref name="replace"/> an already installed dictionary of the same name is removed first (a replace, not a
    /// merge); without it such a name fails the install
    /// (<see cref="DictionaryInstallResult.NameTaken"/>).
    /// </summary>
    public static DictionaryInstallResult Install(IReadOnlyList<string> files, string directory, bool replace)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        DictionaryInstallResult validated = Validate(files);
        if (!validated.IsOk)
        {
            return validated;
        }

        string name = validated.Name!;
        Directory.CreateDirectory(directory);
        string? existing = InstalledNames(directory)
            .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!replace)
            {
                return Failed(name, CoreStrings.Format("DictionaryInstall_AlreadyExists", existing)) with { NameTaken = true };
            }

            Remove(directory, existing);
        }

        string staging = Path.Combine(directory, StagingPrefix + Ulid.NewUlid().ToString());
        try
        {
            Directory.CreateDirectory(staging);
            List<string> staged = new();
            foreach (string source in files)
            {
                string target = Path.Combine(staging, Path.GetFileName(source));
                File.Copy(source, target, overwrite: true);
                staged.Add(target);
            }

            // The copies are what will be installed, so they are the ones checked.
            DictionaryInstallResult copied = Validate(staged);
            if (!copied.IsOk)
            {
                return copied with { Name = name };
            }

            foreach (string source in staged)
            {
                File.Move(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        return new DictionaryInstallResult(Installed: true, name, Array.Empty<string>(), Problem: null);
    }

    /// <summary>
    /// The files of the dictionary <paramref name="name"/> in <paramref name="directory"/> — what
    /// <see cref="Remove"/> would delete: the <c>.aff</c>/<c>.dic</c> pair, the companion files whose name names this
    /// dictionary (<c>hyph_pl_PL.dic</c>, <c>th_pl_PL_v2.dat</c>, <c>README_pl_PL.txt</c>), and — only when this is the
    /// one dictionary left in the folder — the files a whole package shares (<c>dictionaries.xcu</c>, <c>*.tex</c>, a
    /// licence).
    /// </summary>
    public static IReadOnlyList<string> FilesOf(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        List<string> names = InstalledNames(directory).ToList();
        List<string> result = new();
        foreach (string path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(path);
            if (IsPairOf(file, name))
            {
                result.Add(path);
                continue;
            }

            if (!IsPackageFile(path) || names.Any(other => IsPairOf(file, other)))
            {
                continue;
            }

            string? owner = Owner(file, names);
            bool mine = owner is null
                ? names.Count == 1
                : string.Equals(owner, name, StringComparison.OrdinalIgnoreCase);
            if (mine)
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>Deletes every file of the dictionary <paramref name="name"/> (<see cref="FilesOf"/>).</summary>
    public static void Remove(string directory, string name)
    {
        foreach (string path in FilesOf(directory, name))
        {
            File.Delete(path);
        }
    }

    /// <summary>What a file of a dictionary package is, from its name.</summary>
    public static DictionaryFileRole RoleOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string file = Path.GetFileName(path);
        string extension = Path.GetExtension(file).ToLowerInvariant();
        return extension switch
        {
            ".aff" => DictionaryFileRole.Required,
            ".dic" => IsHyphenationFile(file) ? DictionaryFileRole.Hyphenation : DictionaryFileRole.Required,
            ".tex" => DictionaryFileRole.Hyphenation,
            ".dat" or ".idx" => DictionaryFileRole.Thesaurus,
            ".xcu" => DictionaryFileRole.LibreOffice,
            _ => DictionaryFileRole.Documentation,
        };
    }

    // The names of the dictionaries in the folder: every .aff and every word-list .dic found there, even when the
    // pair is incomplete (such an entry is listed with a problem, so the user can see it and delete it).
    private static IReadOnlyList<string> InstalledNames(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => IsAffixFile(path) || IsWordListFile(path))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Why the installed dictionary cannot be used; null when it works. A pair put into the folder by hand is read
    // here for the first time, so this is also where a broken dictionary is caught.
    private static string? ProblemOf(string directory, string name)
    {
        string affPath = Path.Combine(directory, name + ".aff");
        string dicPath = Path.Combine(directory, name + ".dic");
        if (!File.Exists(affPath))
        {
            return CoreStrings.Format("DictionaryInstall_MissingAff_Named", name + ".aff");
        }

        if (!File.Exists(dicPath))
        {
            return CoreStrings.Format("DictionaryInstall_MissingDic", name + ".dic");
        }

        return CheckContent(affPath, dicPath);
    }

    // The content check of a pair: the .aff holds Hunspell directives, the .dic starts with a word count, the pair
    // loads, and the loaded dictionary knows a word of its own word list.
    private static string? CheckContent(string affPath, string dicPath)
    {
        try
        {
            // The .aff may name a code page encoding (SET ISO8859-2); without the provider the library falls back to
            // Latin-1 and reads every non-ASCII word wrong. Validate the way SpellChecker loads, or a dictionary
            // could pass here and fail there.
            LegacyEncodings.Ensure();

            if (!HasAffixDirective(affPath))
            {
                return CoreStrings.Format("DictionaryInstall_AffNoDirectives", Path.GetFileName(affPath));
            }

            Encoding encoding = EncodingOf(affPath);
            string[] lines = File.ReadAllLines(dicPath, encoding);
            string? countLine = lines.FirstOrDefault(line => line.Trim().Length > 0);
            if (countLine is null || !int.TryParse(countLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return CoreStrings.Format("DictionaryInstall_DicNoCount", Path.GetFileName(dicPath));
            }

            WordList wordList = WordList.CreateFromFiles(dicPath, affPath);
            string? word = SampleWord(lines);
            if (word is null)
            {
                return CoreStrings.Format("DictionaryInstall_DicNoWords", Path.GetFileName(dicPath));
            }

            return wordList.Check(word) ? null : CoreStrings.Format("DictionaryInstall_WordNotChecked", word);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or ArgumentException or InvalidOperationException or NotSupportedException or IndexOutOfRangeException)
        {
            return CoreStrings.Format("DictionaryInstall_CannotLoad", ex.Message);
        }
    }

    // A word of the .dic to try the loaded dictionary on: one with no affix flags, so a broken flag table shows up
    // as a word the dictionary does not know. When every entry carries flags, the first entry without them.
    private static string? SampleWord(string[] lines)
    {
        string? withFlags = null;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // An entry is "word/FLAGS" and may carry morphological fields after a tab.
            string entry = line.Split('\t')[0];
            int slash = entry.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                return entry.Length > 0 ? entry : null;
            }

            withFlags ??= slash > 0 ? entry[..slash] : null;
        }

        return withFlags;
    }

    private static bool HasAffixDirective(string affPath)
    {
        // The directives are ASCII, so Latin-1 decodes them from any of the encodings an .aff may be written in
        // (its own SET line is one of the lines looked at here).
        foreach (string line in File.ReadLines(affPath, Encoding.Latin1))
        {
            if (AffixDirectives.Any(directive =>
                line.StartsWith(directive, StringComparison.Ordinal)
                && (line.Length == directive.Length || char.IsWhiteSpace(line[directive.Length]) || directive == "COMPOUND")))
            {
                return true;
            }
        }

        return false;
    }

    // The encoding the .aff names in its SET line (UTF-8 when it names none, or one .NET cannot resolve).
    private static Encoding EncodingOf(string affPath)
    {
        foreach (string line in File.ReadLines(affPath, Encoding.Latin1))
        {
            if (!line.StartsWith("SET", StringComparison.Ordinal) || line.Length <= 3 || !char.IsWhiteSpace(line[3]))
            {
                continue;
            }

            string name = line[3..].Trim();
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }

    private static bool IsAffixFile(string path) =>
        string.Equals(Path.GetExtension(path), ".aff", StringComparison.OrdinalIgnoreCase);

    // A word list is a .dic that is not libhyphen's hyphenation patterns (hyph_*.dic is a companion, not a dictionary).
    private static bool IsWordListFile(string path) =>
        string.Equals(Path.GetExtension(path), ".dic", StringComparison.OrdinalIgnoreCase)
        && !IsHyphenationFile(Path.GetFileName(path));

    private static bool IsHyphenationFile(string fileName) =>
        fileName.StartsWith("hyph_", StringComparison.OrdinalIgnoreCase);

    private static bool IsPairOf(string fileName, string name) =>
        (string.Equals(Path.GetExtension(fileName), ".aff", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(fileName), ".dic", StringComparison.OrdinalIgnoreCase))
        && string.Equals(Path.GetFileNameWithoutExtension(fileName), name, StringComparison.OrdinalIgnoreCase);

    // Which dictionary a companion file names (the most specific one, so hyph_de_DE.dic stays with de_DE even when
    // de_DE_frami is installed too); null when it names none and the file belongs to the package as a whole.
    private static string? Owner(string fileName, IReadOnlyList<string> names)
    {
        string bare = Path.GetFileNameWithoutExtension(fileName);
        return names
            .Where(name => bare.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => name.Length)
            .FirstOrDefault();
    }

    // Whether the file may live in the dictionaries folder at all — the first of the three validation steps.
    private static bool IsPackageFile(string path)
    {
        string file = Path.GetFileName(path);
        string extension = Path.GetExtension(file);
        if (extension.Length == 0)
        {
            return DocumentationNames.Any(name => file.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        }

        return AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static DictionaryInstallResult Failed(string? name, string problem) =>
        new(Installed: false, name, Array.Empty<string>(), problem);

    private static string Join(IReadOnlyList<string?> files) => string.Join(", ", files);
}
