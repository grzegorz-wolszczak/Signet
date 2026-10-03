using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;
using Signet.Core.Search;

namespace Signet.App.ViewModels;

/// <summary>A single row of the multi-file operation report (Replace All / Count All).</summary>
/// <param name="BookPath">Book path of the file.</param>
/// <param name="Count">Number of matches / replacements in the file.</param>
/// <param name="Skipped">Whether the file was skipped (e.g. it is not well-formed).</param>
/// <param name="Reason">Reason for skipping, or <c>null</c>.</param>
public sealed record MultiFileReportRow(string BookPath, int Count, bool Skipped, string? Reason)
{
    /// <summary>Text to display in the report drop-down list.</summary>
    public string Display => Skipped
        ? Strings.Format("FindReplace_ReportRowSkipped", BookPath, Reason)
        : string.Format(CultureInfo.CurrentCulture, "{0} — {1}", BookPath, Count);
}

/// <summary>
/// Resources + the ready pattern + the replacement text for the "Dry Run Replace All" / "Filter Replacements"
/// preview — the result of <see cref="FindReplaceViewModel.TryBuildReplacePreviewRequest"/>.
/// </summary>
/// <param name="Resources">Resources to search.</param>
/// <param name="SearchRegex">The ready PCRE2 pattern (from <c>SearchRegexBuilder</c>).</param>
/// <param name="ReplaceText">Replacement pattern (expanded per match).</param>
public sealed record ReplacePreviewRequest(
    IReadOnlyList<TextResource> Resources,
    string SearchRegex,
    string ReplaceText);

/// <summary>
/// View model of the "Find &amp; Replace" panel: the current-file mode (through
/// <see cref="CodeTabViewModel"/>) and the multi-file mode (the <see cref="LookWhere"/>
/// scopes — through <see cref="IMultiFileSearchHost"/> and
/// <see cref="MultiFileFindEngine"/>).
/// </summary>
public sealed class FindReplaceViewModel : ViewModelBase
{
    private const int MessageSeconds = 20;

    private readonly SettingsStore _settings;
    private readonly IStatusBarService _statusBar;
    private readonly Func<CodeTabViewModel?> _activeCodeTab;
    private readonly IMultiFileSearchHost _host;
    private readonly MultiFileFindEngine _multiFile = new();

    private CodeTabViewModel? _attachedTab;

    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private SearchMode _mode = SearchMode.Normal;
    private SearchDirection _direction = SearchDirection.Down;
    private LookWhere _lookWhere = LookWhere.CurrentFile;
    private string _reportSummary = string.Empty;
    private bool _forceCurrentFile;
    private bool _optionWrap = true;
    private bool _regexDotAll;
    private bool _regexMinimalMatch;
    private bool _regexUnicodeProperty;
    private bool _regexTextOnly;
    private bool _regexAutoTokenise;
    private bool _highlightAllMatches;
    private bool _restrictToSelection;
    private string _message = string.Empty;
    private string _regexError = string.Empty;

    /// <summary>Creates the view model and loads the persisted state.</summary>
    public FindReplaceViewModel(
        SettingsStore settings,
        IStatusBarService statusBar,
        Func<CodeTabViewModel?> activeCodeTab,
        IMultiFileSearchHost host)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _activeCodeTab = activeCodeTab ?? throw new ArgumentNullException(nameof(activeCodeTab));
        _host = host ?? throw new ArgumentNullException(nameof(host));

        FindReplaceSettings stored = settings.GetFindReplaceSettings();
        foreach (string item in stored.FindHistory)
        {
            FindHistory.Add(item);
        }

        foreach (string item in stored.ReplaceHistory)
        {
            ReplaceHistory.Add(item);
        }

        _mode = stored.Mode;
        _direction = stored.Direction;
        _lookWhere = stored.LookWhere;
        _optionWrap = stored.OptionWrap;
        _regexDotAll = stored.RegexDotAll;
        _regexMinimalMatch = stored.RegexMinimalMatch;
        _regexUnicodeProperty = stored.RegexUnicodeProperty;
        _regexTextOnly = stored.RegexTextOnly;
        _regexAutoTokenise = stored.RegexAutoTokenise;
        _highlightAllMatches = stored.HighlightAllMatches;

        FindNextCommand = new RelayCommand(() => FindNext());
        FindPreviousCommand = new RelayCommand(() => FindPrevious());
        ReplaceCommand = new RelayCommand(() => Replace());
        ReplaceFindCommand = new RelayCommand(() => ReplaceFind());
        ReplaceAllCommand = new RelayCommand(() => ReplaceAll());
        CountCommand = new RelayCommand(() => Count());
        CloseReportCommand = new RelayCommand(ClearReport);
        RestartCommand = new RelayCommand(Restart);

        LookWhereOptions = new ObservableCollection<string>(LookWhereLabels);
    }

    /// <summary>History of the Find field (newest first) — the source for the drop-down list.</summary>
    public ObservableCollection<string> FindHistory { get; } = new();

    /// <summary>History of the Replace field (newest first).</summary>
    public ObservableCollection<string> ReplaceHistory { get; } = new();

    /// <summary>Search text / pattern.</summary>
    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value ?? string.Empty))
            {
                // The Count/Replace All report concerns the previous pattern — it is no longer current.
                ClearReport();
                UpdateRegexValidity();
            }
        }
    }

    /// <summary>Replacement text.</summary>
    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value ?? string.Empty);
    }

    /// <summary>Search mode (index: 0 Normal, 1 Case Sensitive, 2 Regex).</summary>
    public int ModeIndex
    {
        get => (int)_mode;
        set
        {
            if (SetProperty(ref _mode, (SearchMode)value, nameof(ModeIndex)))
            {
                OnPropertyChanged(nameof(IsRegexMode));
                UpdateRegexValidity();
                PersistOptions();
            }
        }
    }

    /// <summary>Search direction (index: 0 Down, 1 Up).</summary>
    public int DirectionIndex
    {
        get => (int)_direction;
        set
        {
            if (SetProperty(ref _direction, (SearchDirection)value, nameof(DirectionIndex)))
            {
                PersistOptions();
            }
        }
    }

    /// <summary>Labels of the search scopes in the order of <see cref="LookWhere"/>.</summary>
    private static string[] LookWhereLabels =>
    [
        Strings.Get("FindReplace_LookInCurrentFile"),
        Strings.Get("FindReplace_LookInAllHtml"),
        Strings.Get("FindReplace_LookInSelectedHtml"),
        Strings.Get("FindReplace_LookInTabbedHtml"),
        Strings.Get("FindReplace_LookInAllCss"),
        Strings.Get("FindReplace_LookInSelectedCss"),
        Strings.Get("FindReplace_LookInTabbedCss"),
        Strings.Get("FindReplace_LookInOpf"),
        Strings.Get("FindReplace_LookInNcx"),
        Strings.Get("FindReplace_LookInSelectedSvg"),
        Strings.Get("FindReplace_LookInSelectedJs"),
        Strings.Get("FindReplace_LookInSelectedOtherXml"),
    ];

    /// <summary>Recomputes the "Look in" scope labels after a language change (preserving the selection).</summary>
    public void RefreshLocalizedTexts()
    {
        string[] labels = LookWhereLabels;
        for (int i = 0; i < labels.Length && i < LookWhereOptions.Count; i++)
        {
            LookWhereOptions[i] = labels[i];
        }

        OnPropertyChanged(nameof(LookWhereIndex));
    }

    /// <summary>Media types treated as JavaScript.</summary>
    internal static readonly string[] JavascriptMediaTypes =
    {
        "application/javascript", "text/javascript", "application/x-javascript", "application/ecmascript",
    };

    /// <summary>Media types of "other XML files".</summary>
    internal static readonly string[] MiscXmlMediaTypes =
    {
        "application/ttml+xml", "application/smil+xml", "application/smil", "application/pls+xml",
        "application/oebps-page-map+xml", "application/vnd.adobe-page-map+xml",
        "application/adobe-page-template+xml", "application/vnd.adobe-page-template+xml",
        "application/xml", "text/xml",
    };

    /// <summary>Options of the "Look in" list (search scope).</summary>
    public ObservableCollection<string> LookWhereOptions { get; }

    /// <summary>Selected search scope (index per <see cref="LookWhere"/>).</summary>
    public int LookWhereIndex
    {
        get => (int)_lookWhere;
        set
        {
            if (SetProperty(ref _lookWhere, (LookWhere)value, nameof(LookWhereIndex)))
            {
                _multiFile.Reset();
                ClearReport();
                OnPropertyChanged(nameof(IsMultiFileScope));
                PersistOptions();
            }
        }
    }

    /// <summary>Whether the selected scope covers more than the current file.</summary>
    public bool IsMultiFileScope => _lookWhere != LookWhere.CurrentFile;

    /// <summary>Report rows of the last multi-file operation (Replace All / Count All).</summary>
    public ObservableCollection<MultiFileReportRow> ReportRows { get; } = new();

    /// <summary>Summary of the last multi-file operation (empty when there is no report).</summary>
    public string ReportSummary
    {
        get => _reportSummary;
        private set
        {
            if (SetProperty(ref _reportSummary, value))
            {
                OnPropertyChanged(nameof(HasReport));
            }
        }
    }

    /// <summary>Whether there is anything to show in the multi-file report.</summary>
    public bool HasReport => _reportSummary.Length > 0;

    /// <summary>Whether regex mode is active (controls the visibility of the regex options).</summary>
    public bool IsRegexMode => _mode == SearchMode.Regex;

    /// <summary>Search wrap-around.</summary>
    public bool OptionWrap
    {
        get => _optionWrap;
        set
        {
            if (SetProperty(ref _optionWrap, value))
            {
                PersistOptions();
            }
        }
    }

    /// <summary>Regex: <c>(?s)</c> — the dot also matches a newline.</summary>
    public bool RegexDotAll
    {
        get => _regexDotAll;
        set
        {
            if (SetProperty(ref _regexDotAll, value))
            {
                UpdateRegexValidity();
                PersistOptions();
            }
        }
    }

    /// <summary>Regex: <c>(?U)</c> — quantifiers are lazy by default.</summary>
    public bool RegexMinimalMatch
    {
        get => _regexMinimalMatch;
        set
        {
            if (SetProperty(ref _regexMinimalMatch, value))
            {
                UpdateRegexValidity();
                PersistOptions();
            }
        }
    }

    /// <summary>Regex: <c>(*UCP)</c> — shorthand classes follow Unicode properties.</summary>
    public bool RegexUnicodeProperty
    {
        get => _regexUnicodeProperty;
        set
        {
            if (SetProperty(ref _regexUnicodeProperty, value))
            {
                UpdateRegexValidity();
                PersistOptions();
            }
        }
    }

    /// <summary>"Search in text, not tags" — skips the content of tags in XML resources.</summary>
    public bool RegexTextOnly
    {
        get => _regexTextOnly;
        set
        {
            if (SetProperty(ref _regexTextOnly, value))
            {
                UpdateRegexValidity();
                PersistOptions();
            }
        }
    }

    /// <summary>
    /// "Auto Tokenise" — in Regex mode, when the text selected in Code View is put into the
    /// Find field (<see cref="SeedFindFromSelection"/>), metacharacters are escaped
    /// automatically so that the selection can be searched for literally without manual
    /// escaping.
    /// </summary>
    public bool RegexAutoTokenise
    {
        get => _regexAutoTokenise;
        set
        {
            if (SetProperty(ref _regexAutoTokenise, value))
            {
                PersistOptions();
            }
        }
    }

    /// <summary>
    /// "Highlight all matches" — besides the current match (the selection after Find Next)
    /// highlights the remaining matches in the active file. Toggling works immediately:
    /// enabling it highlights the matches of the current Find text, disabling it hides them.
    /// </summary>
    public bool HighlightAllMatches
    {
        get => _highlightAllMatches;
        set
        {
            if (!SetProperty(ref _highlightAllMatches, value))
            {
                return;
            }

            PersistOptions();
            if (_activeCodeTab() is not { } tab)
            {
                return;
            }

            if (value && !string.IsNullOrEmpty(_findText) && !IsMultiFileMode() && IsPatternValid(LastActivePattern()))
            {
                tab.HighlightSearchMatches(LastActivePattern());
            }
            else if (!value)
            {
                tab.SetSearchHighlights(Array.Empty<(int Start, int End)>());
            }
        }
    }

    private static bool IsPatternValid(string pattern) => PcreCache.Instance.GetObject(pattern).IsValid;

    /// <summary>
    /// "Restrict to selection" — restricts the search to the selected fragment by
    /// marking it as "marked text" on the active tab.
    /// </summary>
    public bool RestrictToSelection
    {
        get => _restrictToSelection;
        set
        {
            if (_restrictToSelection == value)
            {
                return;
            }

            CodeTabViewModel? tab = _activeCodeTab();
            if (value)
            {
                if (tab is null || tab.SelectionEnd <= tab.SelectionStart)
                {
                    SetMessage(Strings.Get("FindReplace_SelectToRestrict"));
                    return;
                }

                tab.ToggleMarkSelection();
            }
            else
            {
                tab?.ClearMarkedText();
            }

            _restrictToSelection = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Result message (number of replacements, "not found", pattern error).</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Description of the regex pattern error (empty when the pattern is valid or the mode is not regex).</summary>
    public string RegexError
    {
        get => _regexError;
        private set
        {
            if (SetProperty(ref _regexError, value))
            {
                OnPropertyChanged(nameof(HasRegexError));
            }
        }
    }

    /// <summary>Whether the regex pattern is invalid.</summary>
    public bool HasRegexError => _regexError.Length > 0;

    /// <summary>Find Next.</summary>
    public IRelayCommand FindNextCommand { get; }

    /// <summary>Find Previous.</summary>
    public IRelayCommand FindPreviousCommand { get; }

    /// <summary>Replace (replace and stay).</summary>
    public IRelayCommand ReplaceCommand { get; }

    /// <summary>Replace/Find (replace and find the next one).</summary>
    public IRelayCommand ReplaceFindCommand { get; }

    /// <summary>Replace All.</summary>
    public IRelayCommand ReplaceAllCommand { get; }

    /// <summary>Count All.</summary>
    public IRelayCommand CountCommand { get; }

    /// <summary>Closes (clears) the report of the last multi-file operation.</summary>
    public IRelayCommand CloseReportCommand { get; }

    /// <summary>"Restart" — see <see cref="Restart"/>.</summary>
    public IRelayCommand RestartCommand { get; }

    /// <summary>
    /// Forgets the previous search — the next Find is a "new search" (in multi-file mode
    /// with a new start file = the current file). Shows the "Search will restart" message.
    /// </summary>
    public void Restart()
    {
        _multiFile.Reset();
        SetMessage(Strings.Get("FindReplace_SearchWillRestart"));
    }

    /// <summary>Attaches the panel to the new active Code View tab (called from the main window).</summary>
    public void AttachToActiveTab(CodeTabViewModel? tab)
    {
        if (ReferenceEquals(_attachedTab, tab))
        {
            return;
        }

        if (_attachedTab is not null)
        {
            _attachedTab.MarkedTextChanged -= OnMarkedTextChanged;
        }

        _attachedTab = tab;

        if (_attachedTab is not null)
        {
            _attachedTab.MarkedTextChanged += OnMarkedTextChanged;
            if (!_highlightAllMatches)
            {
                // A leftover from when the option was enabled — nothing is shown while it is disabled.
                _attachedTab.SetSearchHighlights(Array.Empty<(int Start, int End)>());
            }
        }

        // Changing the active file = a new start file at the next multi-file Find Next.
        _multiFile.Reset();
        SyncRestrictFromTab();
        UpdateRegexValidity();
    }

    /// <summary>
    /// Sets/clears the "marked text" area according to the active tab's current selection
    /// (the "Mark Selected Text" action, Ctrl+Shift+M).
    /// </summary>
    public void ToggleMarkSelection()
    {
        CodeTabViewModel? tab = _activeCodeTab();
        if (tab is null)
        {
            return;
        }

        bool marked = tab.ToggleMarkSelection();
        SetMessage(marked
            ? Strings.Get("FindReplace_SelectionMarked")
            : Strings.Get("FindReplace_SelectionUnmarked"));
    }

    /// <summary>Puts the text selected in the editor into the Find field (Ctrl+F on a selection).</summary>
    public void SeedFindFromSelection()
    {
        CodeTabViewModel? tab = _activeCodeTab();
        if (tab is null || tab.SelectionEnd <= tab.SelectionStart)
        {
            return;
        }

        string selected = tab.DocumentText.Substring(
            tab.SelectionStart, tab.SelectionEnd - tab.SelectionStart);

        if (!selected.Contains('\n') && !selected.Contains('\r'))
        {
            if (_regexAutoTokenise && _mode == SearchMode.Regex)
            {
                selected = RegexTokeniser.TokeniseForRegex(selected, includeNumerics: false);
            }

            FindText = selected;
        }
    }

    /// <summary>Saves the panel's current state to the settings (without writing to disk).</summary>
    public void PersistState(bool panelVisible)
    {
        _settings.SaveFindReplaceSettings(Snapshot() with { PanelVisible = panelVisible });
    }

    /// <summary>Find Next / Find Previous (per <see cref="DirectionIndex"/>).</summary>
    public bool FindNext() => FindInDirection(_direction);

    /// <summary>Searches in the direction opposite to the configured one.</summary>
    public bool FindPrevious() => FindInDirection(Opposite(_direction));

    /// <summary>Replaces the current selection (if it is a match) and stays on it.</summary>
    public bool Replace()
    {
        if (!TryPreparePattern(out string pattern, out CodeTabViewModel? tab))
        {
            return false;
        }

        bool replaced = tab.ReplaceCurrentMatch(pattern, _replaceText, _direction, replaceCurrent: true);
        RememberFind();
        RememberReplace();

        if (replaced)
        {
            SetMessage(string.Empty);
            RefreshHighlights(tab, pattern);
        }
        else
        {
            SetMessage(Strings.Get("FindReplace_NoMatchToReplace"));
        }

        return replaced;
    }

    /// <summary>Replaces the current selection and finds the next match.</summary>
    public bool ReplaceFind()
    {
        if (IsMultiFileMode())
        {
            _activeCodeTab()?.ReplaceCurrentMatch(
                LastActivePattern(), _replaceText, _direction, replaceCurrent: false);
            RememberReplace();
            return FindInDirection(_direction);
        }

        if (!TryPreparePattern(out string pattern, out CodeTabViewModel? tab))
        {
            return false;
        }

        tab.ReplaceCurrentMatch(pattern, _replaceText, _direction, replaceCurrent: false);
        FindResult result = tab.FindNextMatch(pattern, _direction, _optionWrap);
        RememberFind();
        RememberReplace();
        RefreshHighlights(tab, pattern);
        SetMessage(ResultMessage(result));
        return result.Found;
    }

    /// <summary>Replaces all matches — in the current file or in the whole scope.</summary>
    /// <remarks>
    /// Before replacing, a "Before: Replace All" checkpoint is created (except in
    /// selected-text mode), rolled back when nothing was replaced.
    /// </remarks>
    public int ReplaceAll() => WithReplaceAllCheckpoint(ReplaceAllCore);

    private int ReplaceAllCore()
    {
        if (IsMultiFileMode())
        {
            return ReplaceAllMultiFile();
        }

        if (!TryPreparePattern(out string pattern, out CodeTabViewModel? tab))
        {
            return 0;
        }

        ClearReport();
        int count = tab.ReplaceAllMatches(pattern, _replaceText, _direction, _optionWrap);
        RememberFind();
        RememberReplace();
        RefreshHighlights(tab, pattern);
        SetMessage(count == 0
            ? Strings.Get("FindReplace_NoReplacements")
            : Strings.Format("FindReplace_ReplacementsDone", count));
        return count;
    }

    /// <summary>Counts matches — in the current file or in the whole scope.</summary>
    public int Count()
    {
        if (IsMultiFileMode())
        {
            return CountMultiFile();
        }

        if (!TryPreparePattern(out string pattern, out CodeTabViewModel? tab))
        {
            return 0;
        }

        ClearReport();
        int count = tab.CountMatches(pattern, _direction, _optionWrap);
        RememberFind();
        RefreshHighlights(tab, pattern);
        SetMessage(count == 0
            ? Strings.Get("FindReplace_NotFound")
            : Strings.Format("FindReplace_MatchCount", count));
        return count;
    }

    // ---------------------------------------- saved searches --- //

    /// <summary>
    /// Loads a saved search into the panel — the Find/Replace fields and the options from the
    /// "Controls" string.
    /// </summary>
    public void LoadSearch(SearchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsGroup)
        {
            return;
        }

        FindText = entry.Find;
        ReplaceText = entry.Replace;

        SearchControlValues c = SearchControls.Parse(entry.Controls);
        _mode = c.Mode;
        _direction = c.Direction;
        _lookWhere = c.LookWhere;
        _optionWrap = c.Wrap;
        _regexDotAll = c.DotAll;
        _regexMinimalMatch = c.MinimalMatch;
        _regexUnicodeProperty = c.UnicodeProperty;
        _regexTextOnly = c.TextOnly;
        _regexAutoTokenise = c.AutoTokenise;
        _multiFile.Reset();

        OnPropertyChanged(nameof(ModeIndex));
        OnPropertyChanged(nameof(IsRegexMode));
        OnPropertyChanged(nameof(DirectionIndex));
        OnPropertyChanged(nameof(LookWhereIndex));
        OnPropertyChanged(nameof(IsMultiFileScope));
        OnPropertyChanged(nameof(OptionWrap));
        OnPropertyChanged(nameof(RegexDotAll));
        OnPropertyChanged(nameof(RegexMinimalMatch));
        OnPropertyChanged(nameof(RegexUnicodeProperty));
        OnPropertyChanged(nameof(RegexTextOnly));
        OnPropertyChanged(nameof(RegexAutoTokenise));
        UpdateRegexValidity();
        PersistOptions();

        SetMessage(entry.Name.Length == 0
            ? Strings.Get("FindReplace_SearchLoaded")
            : Strings.Format("FindReplace_SearchLoadedNamed", entry.Name));
    }

    /// <summary>
    /// Builds a saved-search entry from the panel's current state.
    /// </summary>
    public SearchEntry CurrentAsEntry(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var values = new SearchControlValues(
            _mode, _direction, _lookWhere, _optionWrap,
            _regexDotAll, _regexMinimalMatch, _regexAutoTokenise, _regexUnicodeProperty, _regexTextOnly);
        return new SearchEntry(false, name, name, _findText, _replaceText, SearchControls.Build(values));
    }

    /// <summary>Runs Find for the entries in turn; stops at the first match.</summary>
    public bool RunSavedFind(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (SearchEntry entry in Leaves(entries))
        {
            LoadSearch(entry);
            if (FindNext())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs Replace for the first entry.</summary>
    public bool RunSavedReplaceCurrent(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SearchEntry? first = Leaves(entries).FirstOrDefault();
        if (first is null)
        {
            return false;
        }

        LoadSearch(first);
        return Replace();
    }

    /// <summary>Runs Replace/Find in turn; stops at the first success.</summary>
    public bool RunSavedReplaceFind(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (SearchEntry entry in Leaves(entries))
        {
            LoadSearch(entry);
            if (ReplaceFind())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs Replace All for all entries in turn, summing the replacements.</summary>
    public int RunSavedReplaceAll(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int total = WithReplaceAllCheckpoint(() =>
        {
            int sum = 0;
            foreach (SearchEntry entry in Leaves(entries))
            {
                LoadSearch(entry);
                sum += ReplaceAllCore();
            }

            return sum;
        });

        SetMessage(total == 0
            ? Strings.Get("FindReplace_NoReplacements")
            : Strings.Format("FindReplace_ReplacementsDone", total));
        return total;
    }

    /// <summary>Runs Count All for all entries in turn, summing the matches.</summary>
    public int RunSavedCountAll(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int total = 0;
        foreach (SearchEntry entry in Leaves(entries))
        {
            LoadSearch(entry);
            total += Count();
        }

        SetMessage(total == 0
            ? Strings.Get("FindReplace_NoMatches")
            : Strings.Format("FindReplace_TotalMatches", total));
        return total;
    }

    /// <summary>Counts matches for a single entry (for the "Counts Report").</summary>
    public int CountForEntry(SearchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LoadSearch(entry);
        return Count();
    }

    // Checkpoint before Replace All — skipped when replacing in selected text;
    // rolled back when nothing was replaced.
    private int WithReplaceAllCheckpoint(Func<int> replaceAll)
    {
        bool checkpoint = !(_activeCodeTab()?.Search.Marked.IsMarked ?? false)
            && _host.HasBook
            && _host.CheckpointBefore(Strings.Get("CheckpointOp_ReplaceAll"));
        int count = replaceAll();
        if (checkpoint && count == 0)
        {
            _host.RewindCheckpoint();
        }

        return count;
    }

    private static IEnumerable<SearchEntry> Leaves(IReadOnlyList<SearchEntry> entries) =>
        entries.Where(e => !e.IsGroup);

    // ------------------------------------- Dry Run / Filter Replacements --- //

    /// <summary>
    /// Builds the replacement preview context from the panel's current state: the resources
    /// to search, the search pattern and the replacement text.
    /// First flushes the open tabs. Returns <c>null</c> and sets <see cref="Message"/>
    /// when there is no search text / book, the scope is empty, or the pattern is invalid.
    /// </summary>
    public ReplacePreviewRequest? TryBuildReplacePreviewRequest()
    {
        _host.FlushOpenTabs();

        if (string.IsNullOrEmpty(_findText))
        {
            SetMessage(Strings.Get("FindReplace_EnterSearchText"));
            return null;
        }

        IReadOnlyList<TextResource> resources;
        bool isXml;

        if (_lookWhere == LookWhere.CurrentFile)
        {
            CodeTabViewModel? tab = _activeCodeTab();
            if (tab?.Resource is not TextResource current)
            {
                SetMessage(Strings.Get("FindReplace_NoActiveCodeTab"));
                return null;
            }

            resources = new[] { current };
            isXml = tab.ResourceIsXml;
        }
        else
        {
            if (!_host.HasBook)
            {
                SetMessage(Strings.Get("FindReplace_NoBook"));
                return null;
            }

            resources = _host.ResolveLookWhere(_lookWhere);
            if (resources.Count == 0)
            {
                SetMessage(Strings.Get("FindReplace_EmptyScope"));
                return null;
            }

            isXml = ScopeIsXml();
        }

        var options = new SearchOptions(_regexDotAll, _regexMinimalMatch, _regexUnicodeProperty, _regexTextOnly);
        string pattern = SearchRegexBuilder.BuildSearchRegex(_findText, _mode, options, isXml);

        var spcre = new Spcre(pattern);
        if (!spcre.IsValid)
        {
            SetMessage(Strings.Format("FindReplace_InvalidRegex", spcre.Error));
            return null;
        }

        RememberFind();
        RememberReplace();
        return new ReplacePreviewRequest(resources, pattern, _replaceText);
    }

    /// <summary>
    /// Applies the rows chosen in "Filter Replacements":
    /// groups by file, for files open in a tab makes a single document edit (one Undo step),
    /// for the rest uses <see cref="TextResource.SetText"/>. Returns the total number of replacements.
    /// </summary>
    public int ApplyChosenReplacements(
        IReadOnlyList<ReplacePreviewRow> rows, IReadOnlyList<TextResource> resources)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(resources);

        if (rows.Count == 0)
        {
            SetMessage(Strings.Get("FindReplace_NoReplacementsChosen"));
            return 0;
        }

        _host.FlushOpenTabs();
        int total = 0;

        foreach (IGrouping<string, ReplacePreviewRow> group in
                 rows.GroupBy(r => r.BookPath, StringComparer.Ordinal))
        {
            TextResource? resource = resources.FirstOrDefault(
                r => string.Equals(r.BookPath, group.Key, StringComparison.Ordinal));
            if (resource is null)
            {
                continue;
            }

            CodeTabViewModel? openTab = _host.FindOpenTab(resource);
            if (openTab is not null)
            {
                (string newText, int count) = ReplacePreview.ApplySelected(openTab.DocumentText, group);
                if (count > 0)
                {
                    openTab.SetDocumentTextSingleUndo(newText);
                    total += count;
                }
            }
            else
            {
                (string newText, int count) = ReplacePreview.ApplySelected(resource.GetText(), group);
                if (count > 0)
                {
                    resource.SetText(newText);
                    total += count;
                }
            }
        }

        RememberFind();
        RememberReplace();
        SetMessage(total == 0
            ? Strings.Get("FindReplace_NoReplacements")
            : Strings.Format("FindReplace_ReplacementsDone", total));
        return total;
    }

    /// <summary>Opens the resource with the given path and places the caret at the offset (double click in "Dry Run").</summary>
    public void OpenAtOffset(string bookPath, int offset) =>
        _host.OpenResourceAtMatch(bookPath, offset, offset);

    private bool FindInDirection(SearchDirection direction)
    {
        if (IsMultiFileMode())
        {
            return FindInDirectionMultiFile(direction);
        }

        if (!TryPreparePattern(out string pattern, out CodeTabViewModel? tab))
        {
            return false;
        }

        ClearReport();
        FindResult result = tab.FindNextMatch(pattern, direction, _optionWrap);
        RememberFind();
        RefreshHighlights(tab, pattern);
        SetMessage(ResultMessage(result));
        return result.Found;
    }

    // ------------------------------------------------------- multi-file --- //

    // Current-file mode also applies when a "marked text" area is active or when the
    // "… in File" actions are invoked.
    private bool IsMultiFileMode() =>
        !_forceCurrentFile &&
        _lookWhere != LookWhere.CurrentFile &&
        !(_activeCodeTab()?.Search.Marked.IsMarked ?? false);

    /// <summary>
    /// Runs a Find &amp; Replace operation forcing current-file mode (the "Find Next in File",
    /// "Replace All in File" actions, etc.).
    /// </summary>
    public T InCurrentFile<T>(Func<FindReplaceViewModel, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _forceCurrentFile = true;
        try
        {
            return operation(this);
        }
        finally
        {
            _forceCurrentFile = false;
        }
    }

    private bool FindInDirectionMultiFile(SearchDirection direction)
    {
        if (!TryPrepareMultiFile(direction, out string pattern, out IReadOnlyList<TextResource> files))
        {
            return false;
        }

        ClearReport();
        _host.FlushOpenTabs();
        CodeTabViewModel? active = _activeCodeTab();

        var request = new MultiFileSearchRequest
        {
            Files = files.Select(r => new MultiFileSearchFile(r.BookPath, r.GetText())).ToList(),
            CurrentBookPath = active?.ResourceBookPath ?? string.Empty,
            CurrentCaret = active?.CaretOffset ?? 0,
            CurrentSelectionStart = active?.SelectionStart ?? 0,
            CurrentSelectionEnd = active?.SelectionEnd ?? 0,
            Pattern = pattern,
            Direction = direction,
            Signature = BuildSignature(direction),
        };

        MultiFileFindResult result = _multiFile.FindNext(request);
        RememberFind();

        if (result.Found)
        {
            _host.OpenResourceAtMatch(result.BookPath, result.Start, result.End);
            if (_activeCodeTab() is { } found &&
                string.Equals(found.ResourceBookPath, result.BookPath, StringComparison.Ordinal))
            {
                // The pattern as in Replace (TryPreparePattern) — for the type of the open file.
                found.RememberSearchMatch(LastActivePattern(), result.Start, result.End);
            }

            SetMessage(string.Empty);
            return true;
        }

        SetMessage(Strings.Get("FindReplace_NotFoundEnd"));
        return false;
    }

    private int CountMultiFile()
    {
        if (!TryPrepareMultiFile(_direction, out string pattern, out IReadOnlyList<TextResource> files))
        {
            return 0;
        }

        _host.FlushOpenTabs();
        IReadOnlyList<SearchOperations.FileSearchResult> perFile =
            SearchOperations.CountInFiles(pattern, files);
        RememberFind();

        var rows = perFile
            .Where(r => r.Count > 0)
            .Select(r => new MultiFileReportRow(r.BookPath, r.Count, Skipped: false, Reason: null))
            .ToList();
        int total = rows.Sum(r => r.Count);

        string summary = total == 0
            ? Strings.Get("FindReplace_NotFoundInScope")
            : Strings.Format("FindReplace_MatchesInFiles", total, rows.Count);
        SetReport(summary, rows);
        SetMessage(summary);
        return total;
    }

    private int ReplaceAllMultiFile()
    {
        if (!TryPrepareMultiFile(_direction, out string pattern, out IReadOnlyList<TextResource> files))
        {
            return 0;
        }

        _host.FlushOpenTabs();

        var rows = new List<MultiFileReportRow>();
        int total = 0;
        int skipped = 0;

        foreach (TextResource resource in files)
        {
            if (!IsWellFormedForReplace(resource, out string reason))
            {
                rows.Add(new MultiFileReportRow(resource.BookPath, 0, Skipped: true, reason));
                skipped++;
                continue;
            }

            int count;
            CodeTabViewModel? openTab = _host.FindOpenTab(resource);
            if (openTab is not null)
            {
                count = openTab.ReplaceAllInDocument(pattern, _replaceText);
            }
            else
            {
                string current = resource.GetText();
                (string newText, int replaced) =
                    SearchOperations.PerformGlobalReplace(current, pattern, _replaceText);
                if (replaced > 0 && !string.Equals(newText, current, StringComparison.Ordinal))
                {
                    resource.SetText(newText);
                }

                count = replaced;
            }

            total += count;
            if (count > 0)
            {
                rows.Add(new MultiFileReportRow(resource.BookPath, count, Skipped: false, Reason: null));
            }
        }

        RememberFind();
        RememberReplace();

        string summary = total == 0
            ? Strings.Get("FindReplace_NoReplacements")
            : Strings.Format("FindReplace_ReplacedInFiles", total, rows.Count(r => !r.Skipped));
        if (skipped > 0)
        {
            summary += " " + Strings.Format("FindReplace_FilesSkipped", skipped);
        }

        SetReport(summary, rows);
        SetMessage(summary);
        return total;
    }

    private bool TryPrepareMultiFile(
        SearchDirection direction, out string pattern, out IReadOnlyList<TextResource> files)
    {
        pattern = string.Empty;
        files = Array.Empty<TextResource>();

        if (!_host.HasBook)
        {
            SetMessage(Strings.Get("FindReplace_NoBook"));
            return false;
        }

        if (string.IsNullOrEmpty(_findText))
        {
            SetMessage(Strings.Get("FindReplace_EnterSearchText"));
            return false;
        }

        var options = new SearchOptions(_regexDotAll, _regexMinimalMatch, _regexUnicodeProperty, _regexTextOnly);
        pattern = SearchRegexBuilder.BuildSearchRegex(_findText, _mode, options, ScopeIsXml());

        var spcre = new Spcre(pattern);
        if (!spcre.IsValid)
        {
            SetMessage(Strings.Format("FindReplace_InvalidRegex", spcre.Error));
            return false;
        }

        files = ResolveScope(direction);
        if (files.Count == 0)
        {
            SetMessage(Strings.Get("FindReplace_EmptyScope"));
            return false;
        }

        return true;
    }

    private IReadOnlyList<TextResource> ResolveScope(SearchDirection direction)
    {
        IReadOnlyList<TextResource> resources = _host.ResolveLookWhere(_lookWhere);
        return direction == SearchDirection.Up ? resources.Reverse().ToList() : resources;
    }

    private bool ScopeIsXml() => _lookWhere switch
    {
        LookWhere.AllHtmlFiles or LookWhere.SelectedHtmlFiles or LookWhere.TabbedHtmlFiles
            or LookWhere.OpfFile or LookWhere.NcxFile
            or LookWhere.SelectedSvgFiles or LookWhere.SelectedMiscXmlFiles => true,
        LookWhere.AllCssFiles or LookWhere.SelectedCssFiles or LookWhere.TabbedCssFiles
            or LookWhere.SelectedJsFiles => false,
        _ => _activeCodeTab()?.ResourceIsXml ?? false,
    };

    private static bool IsWellFormedForReplace(TextResource resource, out string reason)
    {
        reason = string.Empty;
        if (resource is not (HtmlResource or XmlResource or OpfResource or NcxResource))
        {
            return true;
        }

        WellFormedResult result = WellFormedChecker.Check(resource.GetText(), resource.MediaType);
        if (result.IsWellFormed)
        {
            return true;
        }

        reason = Strings.Format("FindReplace_NotWellFormedReason", result.Message);
        return false;
    }

    private string BuildSignature(SearchDirection direction) => string.Join(
        " ",
        _findText,
        ((int)_lookWhere).ToString(CultureInfo.InvariantCulture),
        ((int)direction).ToString(CultureInfo.InvariantCulture),
        ((int)_mode).ToString(CultureInfo.InvariantCulture),
        OptionsToken());

    private string OptionsToken() => string.Concat(
        _regexDotAll ? "s" : "-",
        _regexMinimalMatch ? "u" : "-",
        _regexUnicodeProperty ? "p" : "-",
        _regexTextOnly ? "t" : "-");

    private string LastActivePattern()
    {
        var options = new SearchOptions(_regexDotAll, _regexMinimalMatch, _regexUnicodeProperty, _regexTextOnly);
        bool isXml = _activeCodeTab()?.ResourceIsXml ?? ScopeIsXml();
        return SearchRegexBuilder.BuildSearchRegex(_findText, _mode, options, isXml);
    }

    private void SetReport(string summary, IReadOnlyList<MultiFileReportRow> rows)
    {
        ReportRows.Clear();
        foreach (MultiFileReportRow row in rows)
        {
            ReportRows.Add(row);
        }

        ReportSummary = summary;
    }

    private void ClearReport()
    {
        if (ReportRows.Count > 0)
        {
            ReportRows.Clear();
        }

        ReportSummary = string.Empty;
    }

    private static string ResultMessage(FindResult result)
    {
        if (!result.Found)
        {
            return Strings.Get("FindReplace_NotFound");
        }

        return result.Wrapped ? Strings.Get("FindReplace_Wrapped") : string.Empty;
    }

    private bool TryPreparePattern(out string pattern, out CodeTabViewModel tab)
    {
        pattern = string.Empty;
        CodeTabViewModel? active = _activeCodeTab();

        if (active is null)
        {
            tab = null!;
            SetMessage(Strings.Get("FindReplace_NoActiveCodeTab"));
            return false;
        }

        tab = active;

        if (string.IsNullOrEmpty(_findText))
        {
            SetMessage(Strings.Get("FindReplace_EnterSearchText"));
            return false;
        }

        var options = new SearchOptions(_regexDotAll, _regexMinimalMatch, _regexUnicodeProperty, _regexTextOnly);
        pattern = SearchRegexBuilder.BuildSearchRegex(_findText, _mode, options, tab.ResourceIsXml);

        var spcre = new Spcre(pattern);
        if (!spcre.IsValid)
        {
            SetMessage(Strings.Format("FindReplace_InvalidRegex", spcre.Error));
            return false;
        }

        return true;
    }

    private void RefreshHighlights(CodeTabViewModel tab, string pattern)
    {
        if (_highlightAllMatches)
        {
            tab.HighlightSearchMatches(pattern);
        }
        else
        {
            tab.SetSearchHighlights(Array.Empty<(int Start, int End)>());
        }
    }

    private void RememberFind()
    {
        if (string.IsNullOrEmpty(_findText))
        {
            return;
        }

        string findText = _findText;
        MoveToFront(FindHistory, findText.Normalize(NormalizationForm.FormC));
        FindText = findText;
        PersistOptions();
    }

    private void RememberReplace()
    {
        string replaceText = _replaceText;
        MoveToFront(ReplaceHistory, replaceText.Normalize(NormalizationForm.FormC));
        ReplaceText = replaceText;
        PersistOptions();
    }

    // Note: the history is the ItemsSource of the AutoCompleteBox. When an item previously chosen
    // from the list disappears from the collection, the control resets SelectedItem and restores its
    // own SearchText in the field — the last text typed by hand — which through the binding overwrites
    // FindText/ReplaceText. That is why the callers (RememberFind/RememberReplace) restore the
    // field text after reordering the history.
    private static void MoveToFront(ObservableCollection<string> history, string value)
    {
        int existing = history.IndexOf(value);
        if (existing >= 0)
        {
            history.RemoveAt(existing);
        }

        history.Insert(0, value);

        while (history.Count > FindReplaceSettings.MaxHistory)
        {
            history.RemoveAt(history.Count - 1);
        }
    }

    private void OnMarkedTextChanged() => SyncRestrictFromTab();

    private void SyncRestrictFromTab()
    {
        bool marked = _attachedTab?.Search.Marked.IsMarked ?? false;
        if (_restrictToSelection != marked)
        {
            _restrictToSelection = marked;
            OnPropertyChanged(nameof(RestrictToSelection));
        }
    }

    private void UpdateRegexValidity()
    {
        if (_mode != SearchMode.Regex || _findText.Length == 0)
        {
            RegexError = string.Empty;
            return;
        }

        bool isXml = _activeCodeTab()?.ResourceIsXml ?? false;
        var options = new SearchOptions(_regexDotAll, _regexMinimalMatch, _regexUnicodeProperty, _regexTextOnly);
        string pattern = SearchRegexBuilder.BuildSearchRegex(_findText, _mode, options, isXml);
        var spcre = new Spcre(pattern);
        RegexError = spcre.IsValid ? string.Empty : spcre.Error;
    }

    private void SetMessage(string message)
    {
        Message = message;
        if (!string.IsNullOrEmpty(message))
        {
            _statusBar.ShowMessage(message, TimeSpan.FromSeconds(MessageSeconds));
        }
    }

    private void PersistOptions() => _settings.SaveFindReplaceSettings(Snapshot());

    private FindReplaceSettings Snapshot() => new()
    {
        FindHistory = FindHistory.ToList(),
        ReplaceHistory = ReplaceHistory.ToList(),
        Mode = _mode,
        Direction = _direction,
        LookWhere = _lookWhere,
        OptionWrap = _optionWrap,
        RegexDotAll = _regexDotAll,
        RegexMinimalMatch = _regexMinimalMatch,
        RegexUnicodeProperty = _regexUnicodeProperty,
        RegexTextOnly = _regexTextOnly,
        RegexAutoTokenise = _regexAutoTokenise,
        HighlightAllMatches = _highlightAllMatches,
        PanelVisible = _settings.GetFindReplaceSettings().PanelVisible,
    };

    private static SearchDirection Opposite(SearchDirection direction) =>
        direction == SearchDirection.Down ? SearchDirection.Up : SearchDirection.Down;
}
