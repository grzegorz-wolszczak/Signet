using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.Core.Misc;
using Signet.Core.Semantics;
using Signet.Core.Localization;
using WeCantSpell.Hunspell;

namespace Signet.Core.Spellcheck;

/// <summary>
/// A spell checking engine on <c>WeCantSpell.Hunspell</c>:
/// the built-in <c>en_US</c> dictionary (<see cref="EmbeddedDictionary"/>) + additional installed
/// dictionaries (<see cref="AppDirectories.HunspellDictionariesDirectory"/>, <c>.aff</c>/<c>.dic</c> pairs,
/// they override built-in ones with the same name) + multiple user dictionaries
/// (<see cref="AppDirectories.UserDictionariesDirectory"/>, word lists, one file = one dictionary).
/// </summary>
/// <remarks>
/// Not a singleton (the instance is managed by DI) —
/// the class takes <see cref="SettingsStore"/> through the constructor; <c>.dic_delta</c> files are
/// not supported; changing the active primary/secondary dictionary is an explicit call to
/// <see cref="SetPrimaryDictionary"/>/<see cref="SetSecondaryDictionary"/> (they also save to
/// <see cref="SettingsStore"/>). Matching a language code → dictionary is case-insensitive
/// — deliberately, since the <c>xml:lang</c>/<c>lang</c> attributes
/// are often written with inconsistent letter case.
/// </remarks>
public sealed class SpellChecker
{
    private readonly SettingsStore _settings;
    private readonly string _hunspellDictionariesDirectory;
    private readonly string _userDictionariesDirectory;

    private readonly Dictionary<string, DictionarySource> _availableDictionaries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WordList> _openDictionaries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _langCodeToDictionary = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredWords = new(StringComparer.Ordinal);

    private WordList? _primary;
    private WordList? _secondary;

    /// <summary>Creates an engine using the default dictionary directories (<see cref="AppDirectories"/>).</summary>
    public SpellChecker(SettingsStore settings)
        : this(settings, AppDirectories.HunspellDictionariesDirectory, AppDirectories.UserDictionariesDirectory)
    {
    }

    /// <summary>Creates an engine on the given dictionary directories (for tests).</summary>
    public SpellChecker(SettingsStore settings, string hunspellDictionariesDirectory, string userDictionariesDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(hunspellDictionariesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDictionariesDirectory);

        _settings = settings;
        _hunspellDictionariesDirectory = hunspellDictionariesDirectory;
        _userDictionariesDirectory = userDictionariesDirectory;

        EnsureDefaultUserDictionaryFile();
        Reload();
    }

    /// <summary>The name of the user dictionary created when there is none.</summary>
    public const string DefaultUserDictionaryName = "default";

    /// <summary>
    /// A readable name of a Hunspell dictionary from its file name, e.g. <c>en_GB</c> → "English - Great Britain",
    /// <c>de_DE_frami</c> → "German - Germany - frami". An unknown code — the file name unchanged.
    /// </summary>
    public static string DisplayName(string dictionaryName)
    {
        ArgumentNullException.ThrowIfNull(dictionaryName);
        string[] parts = dictionaryName.Replace('_', '-').Split('-');
        string name;
        if (parts.Length == 1)
        {
            name = Language.GetLanguageName(parts[0]);
        }
        else
        {
            name = WithExtraParts(Language.GetLanguageName(parts[0] + "-" + parts[1]), 2);
            if (name.Length == 0)
            {
                name = WithExtraParts(Language.GetLanguageName(parts[0]), 1);
            }
        }

        return name.Length == 0 ? dictionaryName : name;

        string WithExtraParts(string baseName, int from) =>
            baseName.Length == 0 ? baseName : string.Concat(new[] { baseName }.Concat(parts.Skip(from).Select(p => " - " + p)));
    }

    /// <summary>The name of the currently configured primary dictionary (<see cref="SettingsStore.Dictionary"/>).</summary>
    public string PrimaryDictionaryName => _settings.Dictionary;

    /// <summary>The name of the currently configured secondary dictionary (empty = none).</summary>
    public string SecondaryDictionaryName => _settings.SecondaryDictionary;

    /// <summary>
    /// Rediscovers the available dictionaries (built-in + installed) and reloads the primary/secondary one
    /// according to the current settings.
    /// </summary>
    public void Reload()
    {
        _openDictionaries.Clear();
        _primary = null;
        _secondary = null;

        DiscoverAvailableDictionaries();
        BuildLangCodeMapping();

        if (_availableDictionaries.ContainsKey(PrimaryDictionaryName))
        {
            _primary = LoadDictionary(PrimaryDictionaryName);
        }

        if (!string.IsNullOrEmpty(SecondaryDictionaryName) && _availableDictionaries.ContainsKey(SecondaryDictionaryName))
        {
            _secondary = LoadDictionary(SecondaryDictionaryName);
        }
    }

    /// <summary>
    /// The sorted list of available dictionary names (built-in + installed in
    /// <see cref="AppDirectories.HunspellDictionariesDirectory"/>). Rescans the directory
    /// on every call (fresh results without a restart).
    /// </summary>
    public IReadOnlyList<string> Dictionaries()
    {
        DiscoverAvailableDictionaries();
        return _availableDictionaries.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Extra characters treated by the dictionary as part of a word (e.g. an apostrophe) — from the
    /// <c>.aff</c> file of the dictionary (the <c>WORDCHARS</c> directive). Without <paramref name="lang"/> —
    /// the primary dictionary; with <paramref name="lang"/> — resolved by language code (as in
    /// <see cref="Check"/>). An empty string if the dictionary is unknown/not loaded.
    /// </summary>
    public string GetWordChars(string? lang = null)
    {
        string? dictionaryName = string.IsNullOrEmpty(lang) ? PrimaryDictionaryName : ResolveLangDictionary(new[] { lang });
        if (string.IsNullOrEmpty(dictionaryName))
        {
            return string.Empty;
        }

        if (!_availableDictionaries.ContainsKey(dictionaryName))
        {
            return string.Empty;
        }

        WordList dictionary = GetOrLoadDictionary(dictionaryName);
        return dictionary.Affix.WordChars.ToString();
    }

    /// <summary>The sorted list of user dictionary file names.</summary>
    public IReadOnlyList<string> UserDictionaries()
    {
        if (!Directory.Exists(_userDictionariesDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(_userDictionariesDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Sets the primary dictionary: saves it in <see cref="SettingsStore"/> and loads it (if available).
    /// </summary>
    public void SetPrimaryDictionary(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _settings.Dictionary = name;
        _primary = _availableDictionaries.ContainsKey(name) ? LoadDictionary(name) : null;
        BuildLangCodeMapping();
    }

    /// <summary>
    /// Sets the secondary dictionary: saves it in <see cref="SettingsStore"/> and loads it (an empty
    /// string removes the secondary dictionary).
    /// </summary>
    public void SetSecondaryDictionary(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _settings.SecondaryDictionary = name;
        _secondary = name.Length > 0 && _availableDictionaries.ContainsKey(name) ? LoadDictionary(name) : null;
        BuildLangCodeMapping();
    }

    /// <summary>
    /// Checks the spelling of a word. Without <paramref name="langs"/> — the primary + secondary dictionary.
    /// With <paramref name="langs"/> (candidate language codes, in order of
    /// preference, e.g. from <c>xml:lang</c>) — the first dictionary matched by language code; no
    /// match is treated as correct spelling.
    /// </summary>
    public bool Check(string word, IReadOnlyList<string>? langs = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        string safe = NormalizeForSpelling(word);
        if (IsIgnored(word))
        {
            return true;
        }

        if (langs is null || langs.Count == 0)
        {
            if (_primary is null)
            {
                return true;
            }

            return _primary.Check(safe) || (_secondary?.Check(safe) ?? false);
        }

        string? dictionaryName = ResolveLangDictionary(langs);
        if (dictionaryName is null)
        {
            return true;
        }

        WordList dictionary = GetOrLoadDictionary(dictionaryName);
        return dictionary.Check(safe);
    }

    /// <summary>
    /// Correction suggestions. Without <paramref name="langs"/> — the primary and secondary dictionary (up to 4 from
    /// each). With <paramref name="langs"/> — the dictionary resolved by language
    /// code as in <see cref="Check"/> (up to 4 suggestions); no match returns an empty list.
    /// In both cases the sentence-ending period is preserved if the word ended with it
    /// (and it is the only one in the word).
    /// </summary>
    public IReadOnlyList<string> Suggest(string word, IReadOnlyList<string>? langs = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        List<string> suggestions = new();
        string safe = NormalizeForSpelling(word);
        bool possibleEndOfSentence = word.EndsWith('.') && word.Count(c => c == '.') == 1;

        if (langs is null || langs.Count == 0)
        {
            if (_primary is null)
            {
                return suggestions;
            }

            AppendSuggestions(_primary, safe, possibleEndOfSentence, suggestions);
            if (_secondary is not null)
            {
                AppendSuggestions(_secondary, safe, possibleEndOfSentence, suggestions);
            }

            return suggestions;
        }

        string? dictionaryName = ResolveLangDictionary(langs);
        if (dictionaryName is null)
        {
            return suggestions;
        }

        AppendSuggestions(GetOrLoadDictionary(dictionaryName), safe, possibleEndOfSentence, suggestions);
        return suggestions;
    }

    /// <summary>Clears the session list of ignored words.</summary>
    public void ClearIgnoredWords() => _ignoredWords.Clear();

    /// <summary>Adds a word to the session ignore list (not persisted to disk).</summary>
    public void IgnoreWord(string word) => _ignoredWords.Add(word);

    /// <summary>Whether the word is on the session ignore list.</summary>
    public bool IsIgnored(string word) => _ignoredWords.Contains(word);

    /// <summary>
    /// Adds a word to a user dictionary (the default one if <paramref name="dictionaryName"/>
    /// is empty) and — if that dictionary is enabled — immediately to the primary dictionary in memory.
    /// </summary>
    public void AddToUserDictionary(string word, string? dictionaryName = null)
    {
        if (string.IsNullOrEmpty(word))
        {
            return;
        }

        string dict = string.IsNullOrEmpty(dictionaryName) ? _settings.DefaultUserDictionary : dictionaryName;

        if (_primary is not null && _settings.EnabledUserDictionaries.Contains(dict))
        {
            _primary.Add(NormalizeForSpelling(word));
        }

        List<string> existing = ReadUserDictionaryFile(dict);
        if (existing.Contains(word))
        {
            return;
        }

        Directory.CreateDirectory(_userDictionariesDirectory);
        File.AppendAllLines(UserDictionaryPath(dict), new[] { word });
    }

    /// <summary>The words of a single user dictionary, sorted.</summary>
    public IReadOnlyList<string> UserDictionaryWords(string dictionaryName)
    {
        List<string> words = ReadUserDictionaryFile(dictionaryName);
        words.Sort(StringComparer.Ordinal);
        return words;
    }

    /// <summary>
    /// The words from all enabled user dictionaries (<see cref="SettingsStore.EnabledUserDictionaries"/>),
    /// in dictionary order (without additional global sorting).
    /// </summary>
    public IReadOnlyList<string> AllUserDictionaryWords()
    {
        List<string> words = new();
        foreach (string dict in _settings.EnabledUserDictionaries)
        {
            words.AddRange(UserDictionaryWords(dict));
        }

        return words;
    }

    /// <summary>
    /// The user dictionary that "Add to Dictionary" adds to (<see cref="SettingsStore.DefaultUserDictionary"/>).
    /// </summary>
    public string DefaultUserDictionary => _settings.DefaultUserDictionary;

    /// <summary>Sets the default dictionary.</summary>
    public void SetDefaultUserDictionary(string dictionaryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryName);
        _settings.DefaultUserDictionary = dictionaryName;
    }

    /// <summary>
    /// Validates the name of a new user dictionary (or a new name for the dictionary
    /// <paramref name="currentName"/>): non-empty, valid as a file name on every system and not
    /// taken by another dictionary — case-insensitive.
    /// </summary>
    public UserDictionaryNameProblem ValidateUserDictionaryName(string name, string? currentName = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim().Length == 0)
        {
            return UserDictionaryNameProblem.Empty;
        }

        if (name != name.Trim() || name is "." or ".." || name.EndsWith('.')
            || name.Any(c => char.IsControl(c) || ForbiddenFileNameChars.Contains(c)))
        {
            return UserDictionaryNameProblem.InvalidCharacters;
        }

        bool taken = UserDictionaries().Any(existing =>
            string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(existing, currentName, StringComparison.Ordinal));
        return taken ? UserDictionaryNameProblem.AlreadyExists : UserDictionaryNameProblem.None;
    }

    /// <summary>
    /// Creates an empty user dictionary and enables it right away.
    /// </summary>
    /// <exception cref="ArgumentException">The name does not pass <see cref="ValidateUserDictionaryName"/>.</exception>
    public void CreateUserDictionary(string name)
    {
        ThrowIfInvalidName(name, null);
        Directory.CreateDirectory(_userDictionariesDirectory);
        File.WriteAllBytes(UserDictionaryPath(name), Array.Empty<byte>());
        _settings.EnabledUserDictionaries = _settings.EnabledUserDictionaries.Append(name).ToList();
    }

    /// <summary>
    /// Renames a user dictionary file and moves its enabled state and default-dictionary role
    /// to the new name.
    /// </summary>
    /// <exception cref="ArgumentException">The new name does not pass <see cref="ValidateUserDictionaryName"/>.</exception>
    public void RenameUserDictionary(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldName);
        ThrowIfInvalidName(newName, oldName);
        File.Move(UserDictionaryPath(oldName), UserDictionaryPath(newName));

        _settings.EnabledUserDictionaries = _settings.EnabledUserDictionaries
            .Select(d => string.Equals(d, oldName, StringComparison.Ordinal) ? newName : d)
            .ToList();
        if (string.Equals(_settings.DefaultUserDictionary, oldName, StringComparison.Ordinal))
        {
            _settings.DefaultUserDictionary = newName;
        }
    }

    /// <summary>
    /// Copies a user dictionary under the first free name <c>name_copy</c>(<c>_copy</c>…) and enables
    /// the copy. Returns the name of the copy.
    /// </summary>
    public string CopyUserDictionary(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string copyName = name;
        while (ValidateUserDictionaryName(copyName) == UserDictionaryNameProblem.AlreadyExists)
        {
            copyName += "_copy";
        }

        ThrowIfInvalidName(copyName, null);
        File.Copy(UserDictionaryPath(name), UserDictionaryPath(copyName));
        _settings.EnabledUserDictionaries = _settings.EnabledUserDictionaries.Append(copyName).ToList();
        return copyName;
    }

    /// <summary>
    /// Removes a user dictionary (the file, the enabled state; the default role passes to the first remaining one)
    /// and reloads the primary dictionary so that its words stop being accepted.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is the last user dictionary.</exception>
    public void RemoveUserDictionary(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        IReadOnlyList<string> dictionaries = UserDictionaries();
        if (dictionaries.Count <= 1)
        {
            throw new InvalidOperationException(CoreStrings.Get("Error_CannotRemoveLastUserDictionary"));
        }

        File.Delete(UserDictionaryPath(name));
        _settings.EnabledUserDictionaries = _settings.EnabledUserDictionaries
            .Where(d => !string.Equals(d, name, StringComparison.Ordinal))
            .ToList();
        if (string.Equals(_settings.DefaultUserDictionary, name, StringComparison.Ordinal))
        {
            _settings.DefaultUserDictionary = UserDictionaries()[0];
        }

        Reload();
    }

    /// <summary>
    /// Replaces the words of a user dictionary: a typographic apostrophe is converted to a straight one, empty ones
    /// are skipped, without duplicates, sorted. When a word was removed, reloads the primary dictionary.
    /// </summary>
    public void SetUserDictionaryWords(string dictionaryName, IEnumerable<string> words)
    {
        ArgumentException.ThrowIfNullOrEmpty(dictionaryName);
        ArgumentNullException.ThrowIfNull(words);

        List<string> cleaned = words
            .Select(w => w.Replace('’', '\'').Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToList();
        List<string> previous = ReadUserDictionaryFile(dictionaryName);

        Directory.CreateDirectory(_userDictionariesDirectory);
        File.WriteAllLines(UserDictionaryPath(dictionaryName), cleaned);

        if (previous.Except(cleaned, StringComparer.Ordinal).Any())
        {
            Reload();
        }
        else if (_primary is not null && _settings.EnabledUserDictionaries.Contains(dictionaryName))
        {
            foreach (string word in cleaned.Except(previous, StringComparer.Ordinal))
            {
                _primary.Add(NormalizeForSpelling(word));
            }
        }
    }

    // Characters not allowed in file names on Windows — a set shared by all systems
    // so that dictionaries can be moved between them (Path.GetInvalidFileNameChars depends on the system).
    private static readonly char[] ForbiddenFileNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private void ThrowIfInvalidName(string name, string? currentName)
    {
        UserDictionaryNameProblem problem = ValidateUserDictionaryName(name, currentName);
        if (problem != UserDictionaryNameProblem.None)
        {
            throw new ArgumentException(
                CoreStrings.Format("Error_InvalidUserDictionaryName", name, CoreStrings.Get("UserDictionaryNameProblem_" + problem)),
                nameof(name));
        }
    }

    /// <summary>
    /// Creates the user dictionaries directory and an empty default dictionary file if they do not exist
    /// (the dictionary list is never empty).
    /// </summary>
    private void EnsureDefaultUserDictionaryFile()
    {
        Directory.CreateDirectory(_userDictionariesDirectory);
        string path = UserDictionaryPath(_settings.DefaultUserDictionary);
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
        }
    }

    private WordList GetOrLoadDictionary(string name)
    {
        return _openDictionaries.TryGetValue(name, out WordList? existing) ? existing : LoadDictionary(name);
    }

    private WordList LoadDictionary(string name)
    {
        if (!_availableDictionaries.TryGetValue(name, out DictionarySource? source))
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_UnknownDictionary", name));
        }

        WordList wordList = source.Kind == DictionarySourceKind.Embedded
            ? EmbeddedDictionary.Load(name)
            : WordList.CreateFromFiles(source.DicPath!, source.AffPath!);

        if (string.Equals(name, PrimaryDictionaryName, StringComparison.Ordinal))
        {
            foreach (string word in AllUserDictionaryWords())
            {
                wordList.Add(NormalizeForSpelling(word));
            }
        }

        _openDictionaries[name] = wordList;
        return wordList;
    }

    private void DiscoverAvailableDictionaries()
    {
        _availableDictionaries.Clear();
        _availableDictionaries[EmbeddedDictionary.EnUsName] = new DictionarySource(DictionarySourceKind.Embedded);

        if (!Directory.Exists(_hunspellDictionariesDirectory))
        {
            return;
        }

        foreach (string dicPath in Directory.EnumerateFiles(_hunspellDictionariesDirectory, "*.dic"))
        {
            string name = Path.GetFileNameWithoutExtension(dicPath);
            string affPath = Path.Combine(_hunspellDictionariesDirectory, name + ".aff");
            if (File.Exists(affPath))
            {
                // An installed version overrides the built-in one with the same name.
                _availableDictionaries[name] = new DictionarySource(DictionarySourceKind.Directory, affPath, dicPath);
            }
        }
    }

    private void BuildLangCodeMapping()
    {
        _langCodeToDictionary.Clear();
        foreach (string name in _availableDictionaries.Keys)
        {
            string lc = name.Replace('_', '-');
            _langCodeToDictionary[lc] = name;
            if (lc.Length > 3)
            {
                _langCodeToDictionary[lc[..2]] = name;
            }
        }

    // The two-letter codes of the primary/secondary dictionary override possible collisions —
    // the primary one last, so it wins.
        SetTwoLetterOverride(SecondaryDictionaryName);
        SetTwoLetterOverride(PrimaryDictionaryName);

        void SetTwoLetterOverride(string dictionaryName)
        {
            string code = dictionaryName.Replace('_', '-');
            if (code.Length > 3)
            {
                _langCodeToDictionary[code[..2]] = dictionaryName;
            }
        }
    }

    private string? ResolveLangDictionary(IReadOnlyList<string> langs)
    {
        foreach (string lang in langs)
        {
            if (_langCodeToDictionary.TryGetValue(lang, out string? name))
            {
                return name;
            }
        }

        return null;
    }

    private static void AppendSuggestions(WordList dictionary, string word, bool possibleEndOfSentence, List<string> target)
    {
        int count = 0;
        foreach (string suggestion in dictionary.Suggest(word))
        {
            if (count >= 4)
            {
                break;
            }

            target.Add(possibleEndOfSentence && !suggestion.EndsWith('.') ? suggestion + "." : suggestion);
            count++;
        }
    }

    private List<string> ReadUserDictionaryFile(string dictionaryName)
    {
        string path = UserDictionaryPath(dictionaryName);
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Length > 0).ToList()
            : new List<string>();
    }

    private string UserDictionaryPath(string dictionaryName) => Path.Combine(_userDictionariesDirectory, dictionaryName);

    /// <summary>
    /// Normalization of text before passing it to Hunspell: removes the soft hyphen (U+00AD) and
    /// converts the typographic apostrophe (U+2019) to a straight one (U+0027) — Hunspell dictionaries usually
    /// know only the straight variant.
    /// </summary>
    private static string NormalizeForSpelling(string text) => text.Replace("­", string.Empty).Replace('’', '\'');

    private enum DictionarySourceKind
    {
        Embedded,
        Directory,
    }

    private sealed record DictionarySource(DictionarySourceKind Kind, string? AffPath = null, string? DicPath = null);
}
