using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Reports;
using Signet.Core.Spellcheck;

namespace Signet.App.ViewModels;

/// <summary>
/// A ready-to-display table row of the "Spellcheck Editor" dialog (language name resolved,
/// spelling status as text).
/// </summary>
public sealed record SpellcheckWordRow(
    string Word, string LangCode, string LanguageName, int Count, bool Misspelled, string BookPath, int Position,
    int Line = 0, int OccurrenceNumber = 0)
{
    /// <summary>
    /// The occurrences of the word under its row — one per place it stands in the book, so that each can be gone to
    /// and replaced on its own. Empty for a word that occurs once (its own row is that occurrence) and for the
    /// occurrence rows themselves.
    /// </summary>
    public IReadOnlyList<SpellcheckWordRow> Occurrences { get; private init; } = Array.Empty<SpellcheckWordRow>();

    /// <summary>Whether this row is one occurrence of a word rather than the word itself.</summary>
    public bool IsOccurrence => OccurrenceNumber > 0;

    /// <summary>
    /// The "Word" column: the word itself, or — for an occurrence — the file and the line it stands in.
    /// </summary>
    public string Display => IsOccurrence
        ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{BookPath} : {Line}")
        : Word;

    /// <summary>The "Count" column: how many occurrences the word has, or which occurrence this row is.</summary>
    public string CountDisplay => (IsOccurrence ? OccurrenceNumber : Count).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The "Language" column — empty on an occurrence row, where it would only repeat the word's own.</summary>
    public string LanguageDisplay => IsOccurrence ? string.Empty : LanguageName;

    /// <summary>Text of the "Misspelled?" column (<c>"Yes"</c>/<c>"No"</c>); empty on an occurrence row.</summary>
    public string MisspelledDisplay => IsOccurrence
        ? string.Empty
        : Strings.Get(Misspelled ? "Common_Yes" : "Common_No");

    /// <summary>
    /// Whether this row is one place in the book: an occurrence, or a word that occurs once. Only such a row can be
    /// gone to and replaced by itself ("Replace").
    /// </summary>
    public bool IsSinglePlace => IsOccurrence || Count == 1;

    /// <summary>Builds the display row (with its occurrences) from the raw <see cref="SpellcheckWord"/>.</summary>
    public static SpellcheckWordRow From(SpellcheckWord word)
    {
        string languageName = Signet.Core.Semantics.Language.GetLanguageName(word.Lang, word.Lang);
        SpellcheckWordRow row = new(
            word.Text, word.Lang, languageName, word.Count, word.Misspelled, word.BookPath, word.Position);

        // A word that occurs once is its own occurrence: it needs no expander and no child that repeats it.
        if (word.Count <= 1)
        {
            return row;
        }

        List<SpellcheckWordRow> occurrences = new(word.Occurrences.Count);
        for (int i = 0; i < word.Occurrences.Count; i++)
        {
            SpellcheckOccurrence occurrence = word.Occurrences[i];
            occurrences.Add(new SpellcheckWordRow(
                word.Text, word.Lang, languageName, word.Count, word.Misspelled,
                occurrence.BookPath, occurrence.Position, occurrence.Line, OccurrenceNumber: i + 1));
        }

        return row with { Occurrences = occurrences };
    }
}

/// <summary>One occurrence to replace: which word (and language) stands where, and what takes its place.</summary>
/// <param name="Word">The word as it stands in the book.</param>
/// <param name="Lang">The language the word was found with.</param>
/// <param name="BookPath">The file the occurrence is in.</param>
/// <param name="Position">The 0-based offset of the occurrence in that file.</param>
/// <param name="NewText">What takes its place.</param>
public readonly record struct SpellcheckOccurrenceReplacement(
    string Word, string Lang, string BookPath, int Position, string NewText);

/// <summary>
/// View model of the "Spellcheck Editor" dialog: a table of the unique words of the whole book
/// with occurrence counts and spelling status, a "misspelled only" filter, "Ignore"/"Add to
/// Dictionary" (in batch for the selected rows) and "Change All to..." (replacing one selected word
/// in all files — delegated to <see cref="ChangeAllRequested"/>, because it needs access to the
/// open tabs/Book Browser panel, which this view model does not have).
/// </summary>
/// <remarks>
/// The dialog is a separate modeless window (not a dockable panel), backed by a single persistent
/// view model instance (not rebuilt on every opening — it keeps the filter and the selected
/// dictionary between openings without saving them to <c>SettingsStore</c>). Double-click always
/// navigates to the FIRST occurrence of the word in the book (consistent with "go to" in the
/// Reports dialog). The table is sorted by clicking a column header (in the view). The default user dictionary is always listed under "Dictionaries", even before its
/// file exists, so that "Add to Dictionary" works without creating the dictionary first
/// (consistent with "Add To Default Dictionary" in the Code Editor context menu).
/// </remarks>
public sealed class SpellcheckEditorViewModel : ViewModelBase, IDisposable
{
    private readonly SpellChecker _spellChecker;
    private readonly SettingsStore _settings;

    private Book? _book;
    private IReadOnlyList<SpellcheckWordRow> _allWords = Array.Empty<SpellcheckWordRow>();
    private IReadOnlyList<SpellcheckWordRow> _selectedRows = Array.Empty<SpellcheckWordRow>();

    private bool _showAllWords;
    private int _visibleWordCount;
    private string _filterText = string.Empty;
    private string _message = string.Empty;
    private string _selectedDictionary = string.Empty;
    private string _changeAllText = string.Empty;
    private SpellcheckWordRow? _singleSelectedRow;

    // The row index to select after the next Refresh (Ignore / Add / Change All), or -1.
    private int _reselectIndex = -1;

    /// <summary>Creates the view model; the word list is empty until <see cref="Refresh"/> is called.</summary>
    public SpellcheckEditorViewModel(SpellChecker spellChecker, SettingsStore settings)
    {
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.SettingChanged += OnSettingChanged;

        RefreshCommand = new RelayCommand(() => Refresh(_book));
        IgnoreCommand = new RelayCommand(Ignore);
        AddCommand = new RelayCommand(Add);
        ChangeAllCommand = new RelayCommand(ChangeAll, () => !IsBookOperationBusy);
        ReplaceOneCommand = new RelayCommand(ReplaceOne, () => !IsBookOperationBusy && SingleSelectedRow is { IsSinglePlace: true });
        ExportCsvCommand = new RelayCommand(() => ExportCsvRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised by "Export CSV…" — the view asks for the file and writes <see cref="BuildCsv"/> to it.</summary>
    public event EventHandler? ExportCsvRequested;

    /// <summary>
    /// The fonts of the window (Preferences › Appearance › Spelling Editor › Fonts): the column headers, the "Word"
    /// column and the other cells.
    /// </summary>
    public SpellcheckEditorAppearance Appearance => _settings.SpellcheckEditorAppearance;

    /// <summary>Raised after <see cref="Appearance"/> changed — the window applies the fonts again.</summary>
    public event EventHandler? AppearanceChanged;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (SettingChangeKeys.Is(e, SettingChangeKeys.SpellcheckEditorAppearance)
            || SettingChangeKeys.Is(e, SettingChangeKeys.AppearanceSettingsCleared))
        {
            OnPropertyChanged(nameof(Appearance));
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isBookOperationBusy;

    /// <summary>
    /// Whether another operation on the book is running elsewhere (set once, for the lifetime of this persistent view
    /// model, by <c>MainWindowViewModel</c> from its own <c>IsBookOperationBusy</c>) — disables
    /// <see cref="ChangeAllCommand"/> until it finishes.
    /// </summary>
    public bool IsBookOperationBusy
    {
        get => _isBookOperationBusy;
        set
        {
            if (SetProperty(ref _isBookOperationBusy, value))
            {
                ChangeAllCommand.NotifyCanExecuteChanged();
                ReplaceOneCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Exports the visible rows to a CSV file.</summary>
    public IRelayCommand ExportCsvCommand { get; }

    /// <summary>Hides words written in capitals only (remembered in the settings).</summary>
    public bool HideAllCaps
    {
        get => _settings.SpellcheckEditorHideAllCaps;
        set => SetFilterOption(_settings.SpellcheckEditorHideAllCaps, value, v => _settings.SpellcheckEditorHideAllCaps = v);
    }

    /// <summary>Hides camelCase words (remembered in the settings).</summary>
    public bool HideCamelCase
    {
        get => _settings.SpellcheckEditorHideCamelCase;
        set => SetFilterOption(_settings.SpellcheckEditorHideCamelCase, value, v => _settings.SpellcheckEditorHideCamelCase = v);
    }

    /// <summary>Hides snake_case words (remembered in the settings).</summary>
    public bool HideSnakeCase
    {
        get => _settings.SpellcheckEditorHideSnakeCase;
        set => SetFilterOption(_settings.SpellcheckEditorHideSnakeCase, value, v => _settings.SpellcheckEditorHideSnakeCase = v);
    }

    /// <summary>The number of rows the table shows after filtering.</summary>
    public int VisibleWordCount
    {
        get => _visibleWordCount;
        private set => SetProperty(ref _visibleWordCount, value);
    }

    private void SetFilterOption(bool current, bool value, Action<bool> store, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value)
        {
            return;
        }

        store(value);
        _settings.Save();
        OnPropertyChanged(name);
        ApplyFilter();
    }

    /// <summary>The visible rows as CSV: word, count, language, misspelled (headers in the UI language).</summary>
    public string BuildCsv()
    {
        System.Text.StringBuilder sb = new();
        sb.Append(ReportCsv.WriteLine(new[]
        {
            Strings.Get("SpellcheckEditorWindow_Word"), Strings.Get("ReportsWindow_Count"),
            Strings.Get("SpellcheckEditorWindow_Language"), Strings.Get("SpellcheckEditorWindow_Misspelled"),
        })).Append('\n');
        foreach (SpellcheckWordRow row in Words)
        {
            sb.Append(ReportCsv.WriteLine(new[]
            {
                row.Word, row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), row.LanguageName, row.MisspelledDisplay,
            })).Append('\n');
        }

        return sb.ToString();
    }

    // "NASA", "HTML5": at least two letters and no lowercase one.
    private static bool IsAllCaps(string word) =>
        word.Count(char.IsLetter) >= 2 && !word.Any(char.IsLower);

    // "iPhone", "camelCase": a lowercase letter followed by a capital.
    private static bool IsCamelCase(string word)
    {
        for (int i = 1; i < word.Length; i++)
        {
            if (char.IsLower(word[i - 1]) && char.IsUpper(word[i]))
            {
                return true;
            }
        }

        return false;
    }

    // "snake_case": an underscore between two word characters.
    private static bool IsSnakeCase(string word)
    {
        for (int i = 1; i < word.Length - 1; i++)
        {
            if (word[i] == '_' && char.IsLetterOrDigit(word[i - 1]) && char.IsLetterOrDigit(word[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Raised by "Change All to..." — the actual replacement needs access to the tabs/book from <c>MainWindowViewModel</c>.</summary>
    public event Action<string, string, string>? ChangeAllRequested;

    /// <summary>
    /// Raised by "Replace" — one occurrence (word, bookpath, offset, the new text). Like
    /// <see cref="ChangeAllRequested"/>, the replacement itself belongs to <c>MainWindowViewModel</c>.
    /// </summary>
    public event Action<SpellcheckOccurrenceReplacement>? ReplaceOneRequested;

    /// <summary>Raised on a row double-click — navigation to the first occurrence (book path, offset).</summary>
    public event Action<string, int>? NavigationRequested;

    /// <summary>Raised after "Ignore"/"Add to Dictionary" — open Code Editor tabs should refresh their spelling highlighting.</summary>
    public event EventHandler? DictionaryStateChanged;

    /// <summary>
    /// Raised after the table was rebuilt by "Ignore", "Add to Dictionary" or "Change All": the view should select the
    /// row at this index (the view model already treats it as selected). Like Sigil, the selection keeps its row index,
    /// so the next word moves under it (the last row when the table got shorter).
    /// </summary>
    public event EventHandler<int>? SelectRowRequested;

    /// <summary>Stops following the look settings (the sample of the Preferences preview, when the window closes).</summary>
    public void Dispose() => _settings.SettingChanged -= OnSettingChanged;

    /// <summary>Visible (filtered) table rows.</summary>
    public ObservableCollection<SpellcheckWordRow> Words { get; } = new();

    /// <summary>Names of the user dictionaries available in "Add to Dictionary".</summary>
    public ObservableCollection<string> Dictionaries { get; } = new();

    /// <summary>Suggestions for the single selected word (source of the "Change All to..." field).</summary>
    public ObservableCollection<string> Suggestions { get; } = new();

    /// <summary>Refreshes the table from the current book.</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Ignores the selected words for the session.</summary>
    public IRelayCommand IgnoreCommand { get; }

    /// <summary>Adds the selected words to the chosen user dictionary.</summary>
    public IRelayCommand AddCommand { get; }

    /// <summary>Replaces the single selected word with <see cref="ChangeAllText"/> in all files.</summary>
    public IRelayCommand ChangeAllCommand { get; }

    /// <summary>
    /// Replaces one occurrence with <see cref="ChangeAllText"/>: the selected occurrence row, or a word that occurs
    /// only once. Available only then — with a whole word selected there is nothing to tell which place is meant.
    /// </summary>
    public IRelayCommand ReplaceOneCommand { get; }

    /// <summary>Filter text (shows only words containing this text).</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Whether to show all words (not only misspelled ones). Off by default.</summary>
    public bool ShowAllWords
    {
        get => _showAllWords;
        set
        {
            if (SetProperty(ref _showAllWords, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Result message of the last operation.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>The target user dictionary for "Add to Dictionary".</summary>
    public string SelectedDictionary
    {
        get => _selectedDictionary;
        set => SetProperty(ref _selectedDictionary, value ?? string.Empty);
    }

    /// <summary>Text of the "Change All to..." field (editable, prefilled with the first suggestion).</summary>
    public string ChangeAllText
    {
        get => _changeAllText;
        set => SetProperty(ref _changeAllText, value ?? string.Empty);
    }

    /// <summary>The single selected row (<c>null</c> when the selection is empty or multiple) — for "Change All"/suggestions.</summary>
    public SpellcheckWordRow? SingleSelectedRow
    {
        get => _singleSelectedRow;
        private set
        {
            if (SetProperty(ref _singleSelectedRow, value))
            {
                ReplaceOneCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Sets the currently selected rows (called from the view when the table selection changes).</summary>
    public void SetSelectedWords(IEnumerable<SpellcheckWordRow> rows)
    {
        _selectedRows = rows?.ToList() ?? new List<SpellcheckWordRow>();
        SingleSelectedRow = _selectedRows.Count == 1 ? _selectedRows[0] : null;
        UpdateSuggestions();
    }

    /// <summary>Recomputes the table for the given book (<c>null</c> = no open publication, empty table).</summary>
    public void Refresh(Book? book)
    {
        _book = book;
        UpdateDictionaries();

        _allWords = book is null
            ? Array.Empty<SpellcheckWordRow>()
            : SpellcheckEditorEngine.GetUniqueWords(book, _spellChecker, _settings)
                .Select(SpellcheckWordRow.From)
                .ToList();

        ApplyFilter();

        // The rows were rebuilt — the previous selection refers to rows that are gone.
        int reselect = _reselectIndex;
        _reselectIndex = -1;
        if (reselect >= 0 && Words.Count > 0)
        {
            int index = Math.Min(reselect, Words.Count - 1);
            SetSelectedWords(new[] { Words[index] });
            SelectRowRequested?.Invoke(this, index);
        }
        else
        {
            SetSelectedWords(Array.Empty<SpellcheckWordRow>());
        }
    }

    private int FirstSelectedIndex() =>
        _selectedRows.Count == 0 ? -1 : _selectedRows.Select(Words.IndexOf).Where(i => i >= 0).DefaultIfEmpty(-1).Min();

    /// <summary>Row double-click — requests navigation to the first occurrence of the word (see the class remarks).</summary>
    public void RequestNavigation(SpellcheckWordRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        NavigationRequested?.Invoke(row.BookPath, row.Position);
    }

    private void ApplyFilter()
    {
        IEnumerable<SpellcheckWordRow> visible = _showAllWords ? _allWords : _allWords.Where(w => w.Misspelled);
        bool hideAllCaps = HideAllCaps;
        bool hideCamelCase = HideCamelCase;
        bool hideSnakeCase = HideSnakeCase;
        visible = visible.Where(w =>
            !(hideAllCaps && IsAllCaps(w.Word)) && !(hideCamelCase && IsCamelCase(w.Word)) && !(hideSnakeCase && IsSnakeCase(w.Word)));
        string filter = _filterText.Trim();
        if (filter.Length > 0)
        {
            visible = visible.Where(w => w.Word.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        Words.ReplaceWith(visible);
        VisibleWordCount = Words.Count;
    }

    private void UpdateDictionaries()
    {
        List<string> dicts = _spellChecker.UserDictionaries().ToList();
        if (!dicts.Contains(_settings.DefaultUserDictionary, StringComparer.Ordinal))
        {
            dicts.Insert(0, _settings.DefaultUserDictionary);
        }

        Dictionaries.ReplaceWith(dicts);
        if (_selectedDictionary.Length == 0 || !dicts.Contains(_selectedDictionary, StringComparer.Ordinal))
        {
            SelectedDictionary = _settings.DefaultUserDictionary;
        }
    }

    private void UpdateSuggestions()
    {
        Suggestions.Clear();
        if (_selectedRows.Count != 1)
        {
            ChangeAllText = string.Empty;
            return;
        }

        SpellcheckWordRow row = _selectedRows[0];
        IReadOnlyList<string> suggestions = _spellChecker.Suggest(row.Word, new[] { row.LangCode });
        foreach (string suggestion in suggestions)
        {
            Suggestions.Add(suggestion);
        }

        ChangeAllText = suggestions.Count > 0 ? suggestions[0] : string.Empty;
    }

    private void Ignore()
    {
        if (_selectedRows.Count == 0)
        {
            Message = Strings.Get("Spellcheck_NoWordsSelected");
            return;
        }

        foreach (SpellcheckWordRow row in _selectedRows)
        {
            _spellChecker.IgnoreWord(row.Word);
        }

        Message = Strings.Get("Spellcheck_WordsIgnored");
        DictionaryStateChanged?.Invoke(this, EventArgs.Empty);
        _reselectIndex = FirstSelectedIndex();
        Refresh(_book);
    }

    private void Add()
    {
        if (_selectedRows.Count == 0)
        {
            Message = Strings.Get("Spellcheck_NoWordsSelected");
            return;
        }

        string dictionaryName = _selectedDictionary;
        bool enabled = _settings.EnabledUserDictionaries.Contains(dictionaryName);
        foreach (SpellcheckWordRow row in _selectedRows)
        {
            _spellChecker.AddToUserDictionary(row.Word, dictionaryName);
        }

        Message = enabled
            ? Strings.Get("Spellcheck_WordsAdded")
            : Strings.Get("Spellcheck_WordsAddedDictionaryDisabled");
        DictionaryStateChanged?.Invoke(this, EventArgs.Empty);
        _reselectIndex = FirstSelectedIndex();
        Refresh(_book);
    }

    // The new text of a replacement, or the message saying why there is none.
    private bool TryReadReplacement(out string newWord)
    {
        newWord = _changeAllText.Trim();
        if (newWord.Length == 0)
        {
            Message = Strings.Get("Spellcheck_EnterReplacement");
            return false;
        }

        if (newWord.Contains('<') || newWord.Contains('>') || newWord.Contains('&'))
        {
            Message = Strings.Get("Spellcheck_InvalidReplacement");
            return false;
        }

        return true;
    }

    private void ReplaceOne()
    {
        if (SingleSelectedRow is not { IsSinglePlace: true } row)
        {
            Message = Strings.Get("Spellcheck_SelectOneOccurrence");
            return;
        }

        if (!TryReadReplacement(out string newWord))
        {
            return;
        }

        // The host replaces the occurrence and refreshes the table synchronously; without a refresh nothing is reselected.
        _reselectIndex = FirstSelectedIndex();
        ReplaceOneRequested?.Invoke(new SpellcheckOccurrenceReplacement(row.Word, row.LangCode, row.BookPath, row.Position, newWord));
        _reselectIndex = -1;
    }

    private void ChangeAll()
    {
        if (SingleSelectedRow is not { } row)
        {
            Message = Strings.Get("Spellcheck_SelectOneWord");
            return;
        }

        if (!TryReadReplacement(out string newWord))
        {
            return;
        }

        // The host replaces the word and refreshes the table synchronously; without a refresh nothing is reselected.
        _reselectIndex = FirstSelectedIndex();
        ChangeAllRequested?.Invoke(row.Word, row.LangCode, newWord);
        _reselectIndex = -1;
    }
}
