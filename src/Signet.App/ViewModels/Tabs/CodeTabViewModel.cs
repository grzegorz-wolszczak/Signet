using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Spellcheck;
using Signet.Core;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Code editor tab (Code View). Holds the AvaloniaEdit <see cref="TextDocument"/> and the view-less
/// <see cref="CodeViewModel"/> (working copy of the content, well-formedness, split marker).
/// </summary>
public sealed class CodeTabViewModel : ContentTabViewModel
{
    private readonly CodeViewModel _model;
    private readonly IStatusBarService _statusBar;
    private readonly SettingsStore _settings;
    private readonly SpellChecker _spellChecker;

    private bool? _isWellFormed;
    private WellFormedResult? _wellFormedError;
    private WellFormedWarning? _wellFormedWarning;
    private TagPairHighlight? _tagHighlight;
    private int _caretLine = 1;
    private int _caretColumn = 1;
    private int _caretOffset;
    private int _caretCodepoint = -1;
    private int _selectionStart;
    private int _selectionEnd;
    private bool _suppressSync;
    private string _secondaryStatus = string.Empty;
    private string _caretBlockElement = string.Empty;
    private bool _removeFormattingEnabled;
    private bool _removeTagPairEnabled;
    private ElementMergeCandidate? _mergeCandidate;
    private bool _insertFileEnabled;
    private bool _insertIdEnabled;
    private bool _insertHyperlinkEnabled;
    private readonly List<(int Start, int End)> _searchHighlights = new();
    private string? _searchHighlightPattern;
    private bool _applyingSearchEdit;
    private IReadOnlyList<HtmlWord> _misspelledWords = Array.Empty<HtmlWord>();

    /// <summary>Creates a code editor tab for an entry of the tab model.</summary>
    public CodeTabViewModel(OpenTab tab, IStatusBarService statusBar, SettingsStore settings, SpellChecker spellChecker)
        : base(tab)
    {
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));

        if (tab.Resource is not TextResource text)
        {
            throw new ArgumentException(
                $"Code View requires a text resource, got {tab.Resource.GetType().Name}.", nameof(tab));
        }

        _model = new CodeViewModel(text);
        Document = new TextDocument(_model.Text);
        Document.UndoStack.ClearAll();
        Document.TextChanged += OnDocumentTextChanged;

        _wordWrap = settings.CodeViewWordWrap;

        RefreshWellFormed();
        RefreshSecondaryStatus();
        RefreshFormatState();
        RefreshSpellcheck();
    }

    private bool _wordWrap;

    /// <summary>
    /// Code View colors from the settings for the light or dark theme.
    /// </summary>
    public CodeViewAppearance AppearanceFor(bool dark) =>
        dark ? _settings.CodeViewDarkAppearance : _settings.CodeViewAppearance;

    /// <summary>
    /// Whether to use extended highlighting
    /// (<see cref="SettingsStore.CodeViewExtendedHighlighting"/>).
    /// </summary>
    public bool ExtendedHighlighting => _settings.CodeViewExtendedHighlighting;

    /// <summary>
    /// Checks whether a book path exists in the book — set by <c>TabManager</c> (the tab
    /// does not know the book). <c>null</c> — link targets are not checked.
    /// </summary>
    public Func<string, bool>? BookPathExists { get; set; }

    /// <summary>Book files for link completion — set by <c>TabManager</c>.</summary>
    public Func<IReadOnlyList<LinkCompletionFile>>? BookFiles { get; set; }

    /// <summary>
    /// Text of the book document with the given book path (from the open tab, if any) — for <c>#id</c>
    /// anchor completion. Set by <c>TabManager</c>.
    /// </summary>
    public Func<string, string?>? DocumentTextOf { get; set; }

    /// <summary>
    /// Completions for the caret: file names in <c>href</c>/<c>src</c>/<c>url()</c>
    /// or anchors after <c>#</c>, fuzzy-filtered by the typed text. <c>null</c> — there is
    /// nothing to suggest.
    /// </summary>
    public LinkCompletionResult? GetLinkCompletions(int caretOffset)
    {
        if (BookFiles is not { } files)
        {
            return null;
        }

        string text = Document.Text;
        if (LinkCompletion.Detect(text, caretOffset, Syntax) is not { } context)
        {
            return null;
        }

        IReadOnlyList<LinkCompletionItem> items;
        if (context.AnchorHref is { } href)
        {
            string? target = href.Length == 0
                ? ResourceBookPath
                : LinkReference.ResolveBookPath(href, _model.Resource.Folder);
            string? html = target is null ? null
                : string.Equals(target, ResourceBookPath, StringComparison.Ordinal) ? text
                : DocumentTextOf?.Invoke(target);
            if (html is null)
            {
                return null;
            }

            items = LinkCompletion.AnchorCandidates(html);
        }
        else
        {
            items = LinkCompletion.FileCandidates(files(), ResourceBookPath, context.Kind);
        }

        IReadOnlyList<LinkCompletionItem> matches = LinkCompletion.Filter(items, context.Query);
        return matches.Count == 0 ? null : new LinkCompletionResult(context.ReplaceStart, matches);
    }

    /// <summary>
    /// Whether the target of a reference from <c>href</c>/<c>src</c>/<c>url()</c> of this file exists in the book
    /// (external links and bare fragments — always yes). For extended highlighting.
    /// </summary>
    public bool LinkTargetExists(string reference) =>
        BookPathExists is not { } exists || LinkReference.TargetExists(reference, _model.Resource.Folder, exists);

    /// <summary>Whether the editor wraps long lines (from <see cref="SettingsStore.CodeViewWordWrap"/>).</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value);
    }

    /// <summary>Request: scroll the editor to the given line (1-based) and place the caret there.</summary>
    public event Action<int>? ScrollToLineRequested;

    /// <summary>
    /// Request after a Format menu operation: set the editor selection to <c>[start, end)</c>
    /// (and the caret to <c>end</c>). Arguments: selection start and end (0-based).
    /// </summary>
    public event Action<int, int>? SelectionRequested;

    /// <summary>Request: scroll the editor to the given offset (0-based) and place the caret there (sync with Preview).</summary>
    public event Action<int>? ScrollToOffsetRequested;

    /// <summary>Request: give keyboard focus to the editor control (the "Focus on Code View" action).</summary>
    public event Action? FocusRequested;

    /// <summary>The caret moved — the argument is the 0-based offset in the document (sync to Preview).</summary>
    public event Action<int>? CaretOffsetChanged;

    /// <summary>The document content changed (typing) — for the "live" preview refresh.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// Request after a successful Find: select <c>[start, end)</c>, scroll into view;
    /// the third argument says whether the search "wrapped around" the end/start of the file.
    /// </summary>
    public event Action<int, int, bool>? SearchResultRequested;

    /// <summary>The "marked text" area changed — to refresh the highlighting.</summary>
    public event Action? MarkedTextChanged;

    /// <summary>The list of highlighted Find matches changed.</summary>
    public event Action? SearchHighlightsChanged;

    /// <summary>The list of underlined misspelled words changed.</summary>
    public event Action? MisspelledWordsChanged;

    /// <summary>
    /// Raised after Ctrl+click on a class name in a <c>class="..."</c> attribute — the argument is the
    /// class name under the caret. The host (the tab manager) resolves the target CSS rule and
    /// opens/scrolls the right file.
    /// </summary>
    public event Action<string>? CssClassJumpRequested;

    /// <summary>Raised to jump to a link target inside the book (a relative reference, may have a <c>#fragment</c>).</summary>
    public event Action<string>? LinkJumpRequested;

    /// <summary>Raised to open an external link (<c>http:</c>, <c>mailto:</c>…) in the browser.</summary>
    public event Action<string>? ExternalLinkRequested;

    /// <summary>The AvaloniaEdit document with the resource content (bound to <c>TextEditor.Document</c>).</summary>
    public TextDocument Document { get; }

    /// <summary>Syntax kind (selects the highlighting grammar).</summary>
    public CodeViewSyntax Syntax => _model.Syntax;

    /// <inheritdoc />
    public override bool CanUndo => Document.UndoStack.CanUndo;

    /// <inheritdoc />
    public override bool CanRedo => Document.UndoStack.CanRedo;

    /// <inheritdoc />
    public override bool? IsWellFormed => _isWellFormed;

    /// <summary>Result of the last well-formedness check with a non-zero error position, or <c>null</c>.</summary>
    public WellFormedResult? WellFormedError => _wellFormedError;

    /// <summary>
    /// Non-blocking structural warning of the last well-formedness check (e.g. a missing DOCTYPE), or
    /// <c>null</c>. Shown as a yellow squiggle with a tooltip.
    /// </summary>
    public WellFormedWarning? WellFormedWarning => _wellFormedWarning;

    /// <summary>
    /// The opening/closing tag pair to highlight for the current caret position, or <c>null</c>.
    /// </summary>
    public TagPairHighlight? TagHighlight => _tagHighlight;

    /// <summary>Caret line number (1-based) — for the status bar.</summary>
    public int CaretLine => _caretLine;

    /// <summary>Caret column number (1-based) — for the status bar.</summary>
    public int CaretColumn => _caretColumn;

    /// <summary>Caret offset in the document (0-based) — for inserting a split marker.</summary>
    public int CaretOffset => _caretOffset;

    /// <summary>Formatted caret position for the status bar.</summary>
    public string CaretStatus => CodeViewModel.FormatCaretPosition(_caretLine, _caretColumn, _caretCodepoint);

    /// <summary>
    /// Additional tab status for the status bar — for CSS resources the rule counter,
    /// empty for other kinds.
    /// </summary>
    public string SecondaryStatus => _secondaryStatus;

    /// <summary>
    /// Whether the "Format" menu operations apply to this tab — only for (X)HTML resources
    /// (the Format menu is active only for such tabs).
    /// </summary>
    public bool SupportsFormatting => Syntax == CodeViewSyntax.Html;

    /// <summary>
    /// Name of the deepest block element enclosing the caret (<c>h1</c>–<c>h6</c>/<c>p</c>/…)
    /// or an empty string — drives the "Heading N" / "Normal" check in the Format menu.
    /// </summary>
    public string CaretBlockElement => _caretBlockElement;

    /// <summary>Whether "Remove Formatting" is currently allowed.</summary>
    public bool RemoveFormattingEnabled => _removeFormattingEnabled;

    /// <summary>Whether "Remove Tag Pair" is currently allowed.</summary>
    public bool RemoveTagPairEnabled => _removeTagPairEnabled;

    /// <summary>
    /// Elements in the selection that "Merge Content" can combine,
    /// or <c>null</c> when the selection does not meet the conditions.
    /// </summary>
    public ElementMergeCandidate? MergeCandidate => _mergeCandidate;

    /// <summary>The "Merge Content" menu label — with the element name and count when merging is possible.</summary>
    public string MergeContentText => MergeContentTextFor(_mergeCandidate);

    /// <summary>The "Merge Content" label for the given candidate (without a mnemonic).</summary>
    public static string MergeContentTextFor(ElementMergeCandidate? candidate) =>
        candidate is null
            ? Strings.Get("Action_MainWindow_MergeContent").Replace("&", string.Empty, StringComparison.Ordinal)
            : Strings.Format("CodeViewMenu_MergeContentCount", candidate.ElementName, candidate.Count);

    /// <summary>Whether "Insert File" is currently allowed.</summary>
    public bool InsertFileEnabled => _insertFileEnabled;

    /// <summary>Whether "Insert ID" is currently allowed.</summary>
    public bool InsertIdEnabled => _insertIdEnabled;

    /// <summary>Whether "Insert Hyperlink" is currently allowed.</summary>
    public bool InsertHyperlinkEnabled => _insertHyperlinkEnabled;

    /// <summary>Current value of the <c>id</c> attribute under the caret (to fill in the "Insert ID" dialog).</summary>
    public string CurrentIdValue
    {
        get
        {
            SyncModelFromDocument();
            return CodeInsertOperations.CurrentIdValue(_model.Text, _caretOffset);
        }
    }

    /// <summary>Current value of the <c>href</c> attribute under the caret (to fill in the "Insert Hyperlink" dialog).</summary>
    public string CurrentHrefValue
    {
        get
        {
            SyncModelFromDocument();
            return CodeInsertOperations.CurrentHrefValue(_model.Text, _caretOffset);
        }
    }

    /// <summary>Book path of this tab's resource (for resolving relative paths in "Insert File/Hyperlink").</summary>
    public string ResourceBookPath => _model.Resource.BookPath;

    /// <summary>The "Find &amp; Replace in the current file" engine of this tab.</summary>
    public CodeViewSearch Search { get; } = new();

    /// <summary>Full content of the editor document.</summary>
    public string DocumentText => Document.Text;

    /// <summary>Start of the editor's current selection (0-based).</summary>
    public int SelectionStart => _selectionStart;

    /// <summary>End of the editor's current selection (0-based).</summary>
    public int SelectionEnd => _selectionEnd;

    /// <summary>
    /// Whether this tab's resource is an XML document — decides the "Search in text, not tags" option
    /// for the "current file" mode.
    /// </summary>
    public bool ResourceIsXml
    {
        get
        {
            string mediaType = _model.Resource.MediaType;
            return mediaType.EndsWith("+xml", StringComparison.Ordinal) ||
                   mediaType == "application/xml" ||
                   mediaType == "text/xml";
        }
    }

    /// <summary>Ranges of Find matches to highlight.</summary>
    public IReadOnlyList<(int Start, int End)> SearchHighlights => _searchHighlights;

    /// <summary>
    /// Whether this resource type is subject to spell checking at all — only (X)HTML.
    /// </summary>
    public bool SupportsSpellcheck => Syntax == CodeViewSyntax.Html;

    /// <summary>Ranges of misspelled words to underline in Code View.</summary>
    public IReadOnlyList<(int Start, int End)> MisspelledWordHighlights =>
        _misspelledWords.Select(w => (w.Offset, w.Offset + w.Length)).ToList();

    /// <summary>Performs Find Next / Find Previous on this tab.</summary>
    public FindResult FindNextMatch(string pattern, SearchDirection direction, bool wrap)
    {
        FindResult result = Search.FindNext(
            Document.Text, _selectionStart, _selectionEnd, _caretOffset, pattern, direction, wrap);

        if (result.Found)
        {
            SearchResultRequested?.Invoke(result.Start, result.End, result.Wrapped);
        }

        return result;
    }

    /// <summary>Counts matches of <paramref name="pattern"/> on this tab.</summary>
    public int CountMatches(string pattern, SearchDirection direction, bool wrap) =>
        Search.Count(Document.Text, _caretOffset, pattern, direction, wrap);

    /// <summary>
    /// Replaces the current selection if it matches the last match.
    /// </summary>
    public bool ReplaceCurrentMatch(string pattern, string replacement, SearchDirection direction, bool replaceCurrent)
    {
        ReplaceResult result = Search.ReplaceSelected(
            Document.Text, _selectionStart, _selectionEnd, _caretOffset,
            pattern, replacement, direction, replaceCurrent);

        if (!result.Replaced)
        {
            return false;
        }

        ApplySearchEdit(result.NewText, result.SelectionStart, result.SelectionEnd);
        return true;
    }

    /// <summary>Replaces all matches on this tab.</summary>
    /// <returns>Number of replacements made.</returns>
    public int ReplaceAllMatches(string pattern, string replacement, SearchDirection direction, bool wrap)
    {
        ReplaceAllResult result = Search.ReplaceAll(
            Document.Text, _caretOffset, pattern, replacement, direction, wrap);

        if (result.Count > 0)
        {
            ApplySearchEdit(result.NewText, result.CaretPosition, result.CaretPosition);
        }

        return result.Count;
    }

    /// <summary>
    /// Replaces all matches of <paramref name="pattern"/> in the whole document (regardless
    /// of direction/wrapping/"marked text") — used by the multi-file "Replace All" for files
    /// open in a tab. Keeps a single Undo step in the editor.
    /// </summary>
    /// <returns>Number of replacements made.</returns>
    public int ReplaceAllInDocument(string pattern, string replacement)
    {
        (string newText, int count) = SearchOperations.PerformGlobalReplace(Document.Text, pattern, replacement);

        if (count > 0)
        {
            Search.ResetLastMatch();
            ApplySearchEdit(newText, _caretOffset, _caretOffset);
        }

        return count;
    }

    /// <summary>
    /// Sets the full document content as a single Undo operation — used by "Filter Replacements"
    /// for files open in a tab, when the chosen replacements are applied.
    /// </summary>
    public void SetDocumentTextSingleUndo(string newText)
    {
        if (string.Equals(Document.Text, newText, System.StringComparison.Ordinal))
        {
            return;
        }

        Search.ResetLastMatch();
        ApplySearchEdit(newText, _caretOffset, _caretOffset);
    }

    /// <summary>
    /// Selects <c>[start, end)</c> and scrolls it into view — after the multi-file Find Next "jump"
    /// to a newly opened tab.
    /// </summary>
    public void SelectMatch(int start, int end) => SearchResultRequested?.Invoke(start, end, false);

    /// <summary>
    /// Remembers the match <c>[start, end)</c> selected by the multi-file Find Next, so that
    /// Replace / Replace &amp; Find can replace it.
    /// </summary>
    public void RememberSearchMatch(string pattern, int start, int end)
    {
        if (Search.RememberMatch(Document.Text, pattern, start, end))
        {
            UpdateSelection(start, end);
        }
    }

    /// <summary>
    /// Ctrl+click / "Go To Link Or Style" at position <paramref name="caretOffset"/> (0-based): on a
    /// class name in <c>class="..."</c> raises <see cref="CssClassJumpRequested"/>; on a
    /// link (<c>href</c>/<c>src</c>/<c>url()</c>) — <see cref="LinkJumpRequested"/> or, for an external
    /// link, <see cref="ExternalLinkRequested"/>.
    /// Returns whether anything was raised.
    /// </summary>
    public bool RequestLinkOrClassJumpAt(int caretOffset)
    {
        if (_model.GetClassNameAtCaret(caretOffset) is { Length: > 0 } className)
        {
            CssClassJumpRequested?.Invoke(className);
            return true;
        }

        if (_model.GetLinkAtCaret(caretOffset) is not { Length: > 0 } link)
        {
            return false;
        }

        if (LinkReference.IsExternal(link))
        {
            ExternalLinkRequested?.Invoke(link);
        }
        else
        {
            LinkJumpRequested?.Invoke(link);
        }

        return true;
    }

    /// <summary>"Go To Link Or Style" (F3) at the caret position.</summary>
    public bool GoToLinkOrStyleAtCaret() => RequestLinkOrClassJumpAt(_caretOffset);

    // =====================================================================
    //  Code View context menu
    // =====================================================================

    /// <summary>
    /// Main window services for the context menu (clips, actions, book operations). Set
    /// by <c>TabManager</c>; <c>null</c> (e.g. in tests) = host-dependent items are skipped.
    /// </summary>
    public ICodeTabHost? Host { get; set; }

    /// <summary>Whether the editor has a selection.</summary>
    public bool HasSelection => _selectionEnd > _selectionStart;

    /// <summary>Selected text (empty when there is no selection).</summary>
    public string SelectedText
    {
        get
        {
            int start = Math.Clamp(_selectionStart, 0, Document.TextLength);
            int end = Math.Clamp(_selectionEnd, start, Document.TextLength);
            return Document.GetText(start, end - start);
        }
    }

    /// <summary>Whether the tab is an (X)HTML file — the "Reformat HTML" menu.</summary>
    public bool IsHtmlFlow => Kind == ContentTabKind.Flow;

    /// <summary>Whether the tab is a CSS stylesheet — the "Reformat CSS" / "Rename Selected Class" menu.</summary>
    public bool IsCss => Kind == ContentTabKind.Css;

    /// <summary>
    /// Whether the "Mark Selected Text" item should read "Unmark Marked Text" — no selection with an
    /// existing "marked text" area.
    /// </summary>
    public bool OffersUnmark => !HasSelection && Search.Marked.IsMarked;

    /// <summary>
    /// The class under the caret in the <c>class</c> attribute of an XHTML file (with the chain of tags from the root)
    /// or <c>null</c> — the condition of the "Rename Class…" item.
    /// </summary>
    public ClassAtCaret? ClassAtCaretForRename() =>
        IsHtmlFlow ? ClassRenamer.FindClassAtCaret(Document.Text, _caretOffset) : null;

    /// <summary>
    /// The class under the caret in a selector of a CSS stylesheet or of a <c>&lt;style&gt;</c> block of an XHTML file, or
    /// <c>null</c> — "Rename Class…" computed with the cascade for that source.
    /// </summary>
    public StyleClassAtCaret? StyleClassAtCaretForRename() =>
        IsCss || IsHtmlFlow ? ClassRenamer.FindStyleClassAtCaret(Document.Text, _caretOffset, ResourceBookPath, IsCss) : null;

    /// <summary>
    /// Image reference under the caret (<c>src</c>/<c>xlink:href</c> of an <c>img</c>/<c>image</c> tag)
    /// or <c>null</c> — the condition of the "View Image" / "Open Tab For Image" items.
    /// </summary>
    public string? ImageSourceAtCaret()
    {
        SyncModelFromDocument();
        return _model.GetImageSourceAtCaret(_caretOffset);
    }

    /// <summary>Book path of the image under the caret; for a standalone SVG file — that file.</summary>
    public string? ImageBookPathAtCaret()
    {
        if (ImageSourceAtCaret() is { Length: > 0 } src)
        {
            return LinkReference.IsExternal(src) ? null : LinkReference.ResolveBookPath(LinkReference.Split(src).Path, _model.Resource.Folder);
        }

        return Kind == ContentTabKind.Svg ? ResourceBookPath : null;
    }

    /// <summary>"View Image" — a preview of the image under the caret (or of this SVG file).</summary>
    public void ViewImageAtCaret()
    {
        if (ImageBookPathAtCaret() is { } bookPath)
        {
            Host?.ViewImage(bookPath);
        }
    }

    /// <summary>"Open Tab For Image" — opens a tab for the image under the caret.</summary>
    public void OpenImageTabAtCaret()
    {
        if (ImageSourceAtCaret() is { Length: > 0 } src && !LinkReference.IsExternal(src))
        {
            LinkJumpRequested?.Invoke(src);
        }
    }

    /// <summary>"Toggle Line Wrap Mode" — toggles wrapping only in this tab (not persisted).</summary>
    public void ToggleLineWrapMode() => WordWrap = !WordWrap;

    /// <summary>
    /// "Reformat HTML" for this file: <paramref name="toValid"/> = "Mend Code", otherwise
    /// "Mend and Prettify Code". A single Undo step.
    /// </summary>
    public async Task ReformatHtmlAsync(bool toValid)
    {
        if (Host is null)
        {
            return;
        }

        SyncModelFromDocument();
        string original = Document.Text;
        if (await Host.ReformatHtmlTextAsync(Resource, original, toValid).ConfigureAwait(true) is { } formatted
            && !string.Equals(formatted, original, StringComparison.Ordinal))
        {
            int caret = Math.Min(_caretOffset, formatted.Length);
            ReplaceDocumentText(formatted, caret, caret);
        }
    }

    /// <summary>
    /// "Reformat CSS" — the stylesheet in multi-line or single-line form (a single Undo step).
    /// </summary>
    public void ReformatCss(bool multipleLineFormat)
    {
        string original = Document.Text;
        string formatted = new CssInfo(original).GetReformattedCssText(multipleLineFormat);
        if (!string.Equals(formatted, original, StringComparison.Ordinal))
        {
            int caret = Math.Min(_caretOffset, formatted.Length);
            ReplaceDocumentText(formatted, caret, caret);
        }
    }

    /// <summary>Sets/clears the "marked text" area according to the current selection.</summary>
    public bool ToggleMarkSelection()
    {
        bool marked = Search.MarkSelection(_selectionStart, _selectionEnd);
        MarkedTextChanged?.Invoke();
        return marked;
    }

    /// <summary>Clears the "marked text" area.</summary>
    public bool ClearMarkedText()
    {
        bool wasMarked = Search.ClearMarkedText();
        if (wasMarked)
        {
            MarkedTextChanged?.Invoke();
        }

        return wasMarked;
    }

    /// <summary>
    /// Highlights all matches of the PCRE2 pattern <paramref name="pattern"/> and remembers it —
    /// after every text change (typing, Split Tag, Undo, Reload…) the matches are recomputed
    /// so that highlights do not stay on stale offsets.
    /// </summary>
    public void HighlightSearchMatches(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _searchHighlightPattern = pattern;
        ApplySearchHighlights(ComputeSearchHighlights(pattern));
    }

    /// <summary>
    /// Sets fixed ranges of matches to highlight — without recomputing after an edit
    /// (forgets the pattern from <see cref="HighlightSearchMatches"/>).
    /// </summary>
    public void SetSearchHighlights(IEnumerable<(int Start, int End)> highlights)
    {
        _searchHighlightPattern = null;
        ApplySearchHighlights(highlights);
    }

    private IEnumerable<(int Start, int End)> ComputeSearchHighlights(string pattern) =>
        PcreCache.Instance.GetObject(pattern)
            .GetEveryMatchInfo(Document.Text)
            .Select(match => (match.Offset.Start, match.Offset.End));

    private void RefreshSearchHighlights()
    {
        if (_searchHighlightPattern is { } pattern)
        {
            ApplySearchHighlights(ComputeSearchHighlights(pattern));
        }
    }

    private void ApplySearchHighlights(IEnumerable<(int Start, int End)> highlights)
    {
        _searchHighlights.Clear();
        if (highlights is not null)
        {
            _searchHighlights.AddRange(highlights);
        }

        SearchHighlightsChanged?.Invoke();
    }

    /// <summary>Updates the remembered editor selection (called by the view).</summary>
    public void UpdateSelection(int start, int end)
    {
        if (_selectionStart == start && _selectionEnd == end)
        {
            return;
        }

        _selectionStart = start;
        _selectionEnd = end;
        RefreshFormatState();
    }

    /// <summary>Updates the remembered caret position together with the code point under the caret (called by the view).</summary>
    public void UpdateCaret(int line, int column, int offset, int codepoint)
    {
        bool offsetChanged = _caretOffset != offset;
        _caretOffset = offset;
        RefreshTagHighlight();
        RefreshFormatState();

        if (offsetChanged)
        {
            CaretOffsetChanged?.Invoke(offset);
        }

        if (_caretLine == line && _caretColumn == column && _caretCodepoint == codepoint)
        {
            return;
        }

        _caretLine = line;
        _caretColumn = column;
        _caretCodepoint = codepoint;
        OnPropertyChanged(nameof(CaretLine));
        OnPropertyChanged(nameof(CaretColumn));
        OnPropertyChanged(nameof(CaretStatus));
    }

    private void RefreshTagHighlight()
    {
        SyncModelFromDocument();
        TagPairHighlight? next = _model.GetTagPairHighlight(_caretOffset);
        if (!Nullable.Equals(next, _tagHighlight))
        {
            _tagHighlight = next;
            OnPropertyChanged(nameof(TagHighlight));
        }
    }

    /// <inheritdoc />
    public override void Save()
    {
        SyncModelFromDocument();
        if (_model.SaveToResource())
        {
            SetModified(false);
        }

        RefreshWellFormed();
    }

    /// <inheritdoc />
    public override void Reload()
    {
        _model.ReloadFromResource();
        _suppressSync = true;
        try
        {
            Document.Text = _model.Text;
            Document.UndoStack.ClearAll();
        }
        finally
        {
            _suppressSync = false;
        }

        SetModified(false);
        RefreshWellFormed();
    }

    /// <inheritdoc />
    public override void Undo()
    {
        if (Document.UndoStack.CanUndo)
        {
            Document.UndoStack.Undo();
        }
    }

    /// <inheritdoc />
    public override void Redo()
    {
        if (Document.UndoStack.CanRedo)
        {
            Document.UndoStack.Redo();
        }
    }

    /// <summary>Scrolls the editor to the given line (1-based, clamped to the document range).</summary>
    public void GoToLine(int line)
    {
        int target = Math.Clamp(line, 1, Math.Max(1, Document.LineCount));
        ScrollToLineRequested?.Invoke(target);
    }

    /// <summary>Scrolls the editor to the given offset (0-based, clamped to the document length) — a jump from Preview.</summary>
    public void GoToOffset(int offset)
    {
        int target = Math.Clamp(offset, 0, Document.TextLength);
        ScrollToOffsetRequested?.Invoke(target);
    }

    /// <summary>
    /// Scrolls the editor to the element with the given <paramref name="fragment"/> (the value of the <c>id</c>
    /// attribute or of an old <c>&lt;a name&gt;</c> anchor). An empty fragment / no match → the start of the file.
    /// Navigation from the "Table Of Contents" panel.
    /// </summary>
    public void GoToFragment(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            GoToLine(1);
            return;
        }

        int offset = FindFragmentOffset(Document.Text, fragment);
        if (offset >= 0)
        {
            GoToOffset(offset);
        }
        else
        {
            GoToLine(1);
        }
    }

    /// <summary>Gives keyboard focus to the editor control (the "Focus on Code View" action).</summary>
    public void RequestFocus() => FocusRequested?.Invoke();

    private static int FindFragmentOffset(string text, string fragment)
    {
        foreach (string attr in new[] { "id", "name" })
        {
            foreach (char quote in new[] { '"', '\'' })
            {
                int i = text.IndexOf($"{attr}={quote}{fragment}{quote}", StringComparison.Ordinal);
                if (i >= 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Inserts the section split marker (<see cref="CodeViewModel.SectionMarker"/>) at the given
    /// position (0-based). The change goes through <see cref="Document"/>, so it is undoable.
    /// </summary>
    public void InsertSectionMarker(int offset)
    {
        int clamped = Math.Clamp(offset, 0, Document.TextLength);
        Document.Insert(clamped, CodeViewModel.SectionMarker);
    }

    /// <summary>Inserts the section split marker at the current caret position.</summary>
    public void InsertSectionMarkerAtCaret() => InsertSectionMarker(_caretOffset);

    /// <summary>
    /// Checks XML well-formedness on demand: updates <see cref="IsWellFormed"/> /
    /// <see cref="WellFormedError"/> and shows the result on the status bar. Returns the verdict
    /// (<c>null</c> when the resource is not subject to checking).
    /// </summary>
    public bool? RunWellFormedCheck()
    {
        SyncModelFromDocument();
        WellFormedResult? result = RefreshWellFormed();
        if (result is null)
        {
            return null;
        }

        if (result.Warning is { } warning)
        {
            _statusBar.ShowMessage(
                Strings.Format("CodeView_WellFormedWithWarning", warning.Message),
                TimeSpan.FromSeconds(8),
                NotificationLevel.Warning);
        }
        else
        {
            _statusBar.ShowMessage(
                result.IsWellFormed
                    ? Strings.Get("CodeView_WellFormed")
                    : Strings.Format("CodeView_NotWellFormed", result.Line, result.Column, result.Message),
                TimeSpan.FromSeconds(result.IsWellFormed ? 3 : 8));
        }

        return result.IsWellFormed;
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        // Also on Reload (_suppressSync) — the document text is already new.
        RefreshSearchHighlights();

        if (_suppressSync)
        {
            return;
        }

        SyncModelFromDocument();

        // Typing/Undo clears "marked text" and invalidates the remembered match; edits made by
        // Find & Replace itself are skipped (the engine has already done its bookkeeping).
        if (!_applyingSearchEdit)
        {
            bool wasMarked = Search.Marked.IsMarked;
            Search.NotifyTextChanged();
            if (wasMarked && !Search.Marked.IsMarked)
            {
                MarkedTextChanged?.Invoke();
            }
        }

        SetModified(_model.IsModified);
        RefreshWellFormed();
        RefreshSecondaryStatus();
        RefreshFormatState();
        RefreshSpellcheck();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Spellcheck in Code View (underlining, add/ignore a misspelled word, current word at the caret) ----

    /// <summary>
    /// Refreshes the list of misspelled words after the content/spelling settings change. No effect
    /// when the resource is not subject to checking (<see cref="SupportsSpellcheck"/>) or checking is
    /// disabled (<see cref="SettingsStore.SpellCheck"/>) — the list is then simply empty.
    /// </summary>
    private void RefreshSpellcheck()
    {
        IReadOnlyList<HtmlWord> next = SupportsSpellcheck && _settings.SpellCheck
            ? HtmlSpellCheck.GetMisspelledWords(_spellChecker, _settings, _model.Text)
            : Array.Empty<HtmlWord>();

        _misspelledWords = next;
        MisspelledWordsChanged?.Invoke();
    }

    /// <summary>
    /// Called after the global "Highlight Misspelled Words" setting changes — recomputes
    /// the underlines of this tab.
    /// </summary>
    public void NotifySpellCheckSettingChanged() => RefreshSpellcheck();

    /// <summary>
    /// The misspelled word containing the caret (strictly inside, with no selection) or
    /// exactly matching the current selection — <c>null</c> when none matches.
    /// </summary>
    public HtmlWord? GetMisspelledWordAtCaret()
    {
        if (_selectionStart != _selectionEnd)
        {
            foreach (HtmlWord word in _misspelledWords)
            {
                if (word.Offset == _selectionStart && word.Offset + word.Length == _selectionEnd)
                {
                    return word;
                }
            }

            return null;
        }

        foreach (HtmlWord word in _misspelledWords)
        {
            if (_caretOffset > word.Offset && _caretOffset < word.Offset + word.Length)
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>Correction suggestions for the given word, in its language.</summary>
    public IReadOnlyList<string> GetSuggestionsFor(HtmlWord word) =>
        _spellChecker.Suggest(word.Text, new[] { word.Lang });

    /// <summary>Names of the available user dictionaries (for the "Add To Dictionary" submenu).</summary>
    public IReadOnlyList<string> UserDictionaryNames() => _spellChecker.UserDictionaries();

    /// <summary>
    /// Replaces the given (misspelled) word with the suggested correction, provided the text at its
    /// offset still matches (the document may have changed since the detection).
    /// </summary>
    public void ReplaceMisspelledWord(HtmlWord word, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (word.Offset < 0 || word.Offset + word.Length > Document.TextLength)
        {
            return;
        }

        if (!string.Equals(Document.GetText(word.Offset, word.Length), word.Text, StringComparison.Ordinal))
        {
            return;
        }

        Document.Replace(word.Offset, word.Length, replacement);
    }

    /// <summary>
    /// Adds the misspelled word under the caret to the default user dictionary (the
    /// "Add Misspelled Word" action). No effect when there is no misspelled word under the caret.
    /// </summary>
    public void AddCurrentMisspelledWordToDictionary() => AddCurrentMisspelledWordToDictionary(null);

    /// <summary>Like <see cref="AddCurrentMisspelledWordToDictionary()"/>, but into the given user dictionary.</summary>
    public void AddCurrentMisspelledWordToDictionary(string? dictionaryName)
    {
        if (GetMisspelledWordAtCaret() is not { } word)
        {
            return;
        }

        _spellChecker.AddToUserDictionary(word.Text, dictionaryName);
        RefreshSpellcheck();
    }

    /// <summary>
    /// Ignores (for the session) the misspelled word under the caret (the "Ignore Misspelled Word" action).
    /// No effect when there is no misspelled word under the caret.
    /// </summary>
    public void IgnoreCurrentMisspelledWord()
    {
        if (GetMisspelledWordAtCaret() is not { } word)
        {
            return;
        }

        _spellChecker.IgnoreWord(word.Text);
        RefreshSpellcheck();
    }

    /// <summary>
    /// Jumps to the next misspelled word after the caret (wrapping to the start of the
    /// document when needed) — the "Next Misspelled Word" action (F4).
    /// </summary>
    public void GoToNextMisspelledWord()
    {
        if (_misspelledWords.Count == 0)
        {
            return;
        }

        HtmlWord word = _misspelledWords[0];
        bool wrapped = true;
        foreach (HtmlWord candidate in _misspelledWords)
        {
            if (candidate.Offset > _caretOffset)
            {
                word = candidate;
                wrapped = false;
                break;
            }
        }

        SearchResultRequested?.Invoke(word.Offset, word.Offset + word.Length, wrapped);
    }

    // ---- "Format" menu — routed from MainWindowViewModel to the active tab ----

    /// <summary>Bold — toggles <c>&lt;b&gt;</c> / the style <c>font-weight: bold</c>.</summary>
    public void Bold() => RunToggle("b", "font-weight", "bold");

    /// <summary>Italic — toggles <c>&lt;i&gt;</c> / the style <c>font-style: italic</c>.</summary>
    public void Italic() => RunToggle("i", "font-style", "italic");

    /// <summary>Underline — toggles <c>&lt;u&gt;</c> / the style <c>text-decoration: underline</c>.</summary>
    public void Underline() => RunToggle("u", "text-decoration", "underline");

    /// <summary>Strikethrough — toggles <c>&lt;del&gt;</c> / the style <c>text-decoration: line-through</c>.</summary>
    public void Strikethrough() => RunToggle("del", "text-decoration", "line-through");

    /// <summary>Subscript — toggles <c>&lt;sub&gt;</c>.</summary>
    public void Subscript() => RunToggle("sub", null, null);

    /// <summary>Superscript — toggles <c>&lt;sup&gt;</c>.</summary>
    public void Superscript() => RunToggle("sup", null, null);

    /// <summary>Align left — <c>style="text-align: left"</c>.</summary>
    public void AlignLeft() => RunStyle("text-align", "left");

    /// <summary>Align center.</summary>
    public void AlignCenter() => RunStyle("text-align", "center");

    /// <summary>Align right.</summary>
    public void AlignRight() => RunStyle("text-align", "right");

    /// <summary>Align justify.</summary>
    public void AlignJustify() => RunStyle("text-align", "justify");

    /// <summary>Bulleted list — wraps the selected paragraphs in <c>&lt;ul&gt;</c>.</summary>
    public void InsertBulletedList() => RunList("ul");

    /// <summary>Numbered list — wraps the selected paragraphs in <c>&lt;ol&gt;</c>.</summary>
    public void InsertNumberedList() => RunList("ol");

    /// <summary>Increase indent — wraps the selection in <c>&lt;blockquote&gt;</c>.</summary>
    public void IncreaseIndent() => RunWrap(unwrap: false);

    /// <summary>Decrease indent — removes the enclosing <c>&lt;blockquote&gt;</c>.</summary>
    public void DecreaseIndent() => RunWrap(unwrap: true);

    /// <summary>
    /// Text direction — for EPUB3 the <c>dir</c> attribute, for EPUB2 the <c>direction</c> style.
    /// <paramref name="kind"/>: <c>"ltr"</c>/<c>"rtl"</c>/<c>"default"</c>.
    /// </summary>
    public void TextDirection(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        SyncModelFromDocument();
        bool epub3 = _model.Resource.EpubVersion.StartsWith('3');
        FormatEdit edit = epub3
            ? CodeFormatOperations.SetTextDirection(
                _model.Text, _selectionStart, _selectionEnd, kind == "default" ? string.Empty : kind)
            : CodeFormatOperations.FormatStyle(
                _model.Text, _selectionStart, _selectionEnd, "direction", kind == "default" ? "inherit" : kind);
        ApplyFormat(edit);
    }

    /// <summary>Remove Formatting — removes tags from the selection.</summary>
    public void RemoveFormatting()
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.RemoveFormatting(_model.Text, _selectionStart, _selectionEnd));
    }

    /// <summary>Remove Tag Pair — removes the element enclosing the caret.</summary>
    public void RemoveTagPair()
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.RemoveTagPair(_model.Text, _caretOffset));
    }

    /// <summary>Whether tag structure operations apply — (X)HTML and XML.</summary>
    public bool SupportsTagStructure => Syntax is CodeViewSyntax.Html or CodeViewSyntax.Xml;

    /// <summary>Name of the element enclosing the caret (as a hint in "Rename Tag"), or <c>null</c>.</summary>
    public string? EnclosingTagName
    {
        get
        {
            SyncModelFromDocument();
            return TagStructureOperations.FindEnclosingElement(_model.Text, _caretOffset)?.Name;
        }
    }

    /// <summary>
    /// Auto-closing: when <c>&lt;/</c> was typed right before <paramref name="caretOffset"/>,
    /// appends the name of the nearest enclosing element and <c>&gt;</c>.
    /// Returns the new caret position, or <c>null</c> when nothing was inserted
    /// (disabled in the settings, a syntax other than (X)HTML/XML, nothing to close).
    /// </summary>
    public int? TryAutoCloseTag(int caretOffset)
    {
        if (!SupportsTagStructure || !_settings.CodeViewAutoCloseTags)
        {
            return null;
        }

        string text = Document.Text;
        if (TagStructureOperations.AutoCloseTagName(text, caretOffset) is not { } name)
        {
            return null;
        }

        string insert = name + ">";
        Document.Insert(caretOffset, insert);
        return caretOffset + insert.Length;
    }

    /// <summary>"Jump To Opening Tag" — the caret goes after the <c>&lt;</c> of the opening tag of the enclosing element.</summary>
    public void JumpToOpeningTag()
    {
        SyncModelFromDocument();
        GoToTagOffset(TagStructureOperations.OpeningTagCaret(_model.Text, _caretOffset));
    }

    /// <summary>"Jump To Closing Tag" — the caret goes after the <c>&lt;/</c> of the closing tag.</summary>
    public void JumpToClosingTag()
    {
        SyncModelFromDocument();
        GoToTagOffset(TagStructureOperations.ClosingTagCaret(_model.Text, _caretOffset));
    }

    private void GoToTagOffset(int? offset)
    {
        if (offset is { } target)
        {
            GoToOffset(target);
        }
        else
        {
            _statusBar.ShowMessage(Strings.Get("CodeView_NoEnclosingElement"), TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>"Select Tag Contents" — selects the content of the enclosing element.</summary>
    public void SelectTagContents()
    {
        SyncModelFromDocument();
        if (TagStructureOperations.TagContentsRange(_model.Text, _caretOffset) is { } range)
        {
            SelectMatch(range.Start, range.End);
        }
        else
        {
            _statusBar.ShowMessage(Strings.Get("CodeView_NoClosedEnclosingElement"), TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>"Rename Tag" — renames the enclosing element (the opening and closing tag at once).</summary>
    public void RenameTag(string newName)
    {
        SyncModelFromDocument();
        ApplyFormat(TagStructureOperations.RenameTag(_model.Text, _caretOffset, newName));
    }

    /// <summary>
    /// "Merge Content" — merges the selected adjacent elements of the same kind into one with the
    /// attributes of the first. Warnings (attributes, text and comments between the elements) are
    /// shown earlier by the window layer.
    /// </summary>
    public void MergeContent()
    {
        SyncModelFromDocument();
        ApplyFormat(ElementMerger.Merge(_model.Text, _selectionStart, _selectionEnd));
    }

    /// <summary>
    /// The <c>&lt;span&gt;</c> whose opening or closing tag is under the caret — for the "Remove span" context menu
    /// (<see cref="SpanUnwrapper.Find"/>); <c>null</c> outside such a tag or in a non-HTML tab.
    /// </summary>
    public SpanAtCaret? SpanAtCaret()
    {
        if (Syntax != CodeViewSyntax.Html)
        {
            return null;
        }

        SyncModelFromDocument();
        return SpanUnwrapper.Find(_model.Text, _caretOffset);
    }

    /// <summary>
    /// "Remove span": removes the tags of the span under the caret (or, with <paramref name="all"/>, of every span of
    /// the file that is the same as it) and keeps the content. Undoable.
    /// </summary>
    public void UnwrapSpan(bool all)
    {
        SyncModelFromDocument();
        ApplyFormat(SpanUnwrapper.Unwrap(_model.Text, _caretOffset, all, _caretOffset));
    }

    /// <summary>"Remove span" — how many spans of the whole book are the same as the span under the caret.</summary>
    public int CountSameSpansInBook() =>
        Host is not null && Resource is HtmlResource html ? Host.CountSameSpansInBook(html, _model.Text, _caretOffset) : 0;

    /// <summary>
    /// "Remove span" — plans the removal of <paramref name="scope"/> with its consequences for the styling (shown to
    /// the user for acceptance when not empty). <c>null</c> without a book (the removal then has no checked
    /// consequences — <see cref="ApplySpanRemoval"/> with <c>null</c> still removes in this file).
    /// </summary>
    public SpanRemovalPlan? PlanSpanRemoval(SpanRemovalScope scope)
    {
        SyncModelFromDocument();
        return Host is not null && Resource is HtmlResource html ? Host.PlanSpanRemoval(html, _model.Text, _caretOffset, scope) : null;
    }

    /// <summary>
    /// Carries out "Remove span" of <paramref name="scope"/>: in this file through the editor (undoable), in the
    /// whole book through the host (with a checkpoint) using <paramref name="plan"/>.
    /// </summary>
    public void ApplySpanRemoval(SpanRemovalScope scope, SpanRemovalPlan? plan)
    {
        if (scope == SpanRemovalScope.Book)
        {
            if (plan is not null)
            {
                Host?.ApplySpanRemovalInBook(plan);
            }

            return;
        }

        UnwrapSpan(all: scope == SpanRemovalScope.File);
    }

    /// <summary>"Split Tag" — splits the enclosing element at the caret.</summary>
    public void SplitTag()
    {
        SyncModelFromDocument();
        ApplyFormat(TagStructureOperations.SplitTag(_model.Text, _caretOffset));
    }

    /// <summary>Insert Closing Tag — closes the nearest open tag.</summary>
    public void InsertClosingTag()
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.InsertClosingTag(_model.Text, _caretOffset));
    }

    /// <summary>Change Case — changes the letter case in the selection.</summary>
    public void ChangeCasing(Casing casing)
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.ChangeCase(_model.Text, _selectionStart, _selectionEnd, casing));
    }

    /// <summary>Whether Toggle Comment (Ctrl+Shift+/) makes sense for the file's current syntax.</summary>
    public bool SupportsCommentToggle => CodeFormatOperations.IsCommentSyntaxSupported(Syntax);

    /// <summary>Toggle Comment — comments/uncomments the selection (or the current line).</summary>
    public void ToggleComment()
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.ToggleComment(_model.Text, _selectionStart, _selectionEnd, Syntax));
    }

    /// <summary>
    /// Heading — changes the block tag to <paramref name="element"/> (<c>h1</c>–<c>h6</c> or <c>p</c>).
    /// </summary>
    public void HeadingStyle(string element, bool preserveAttributes)
    {
        ArgumentException.ThrowIfNullOrEmpty(element);
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.FormatBlock(
            _model.Text, _selectionStart, _selectionEnd, element, preserveAttributes));
    }

    // ---- "Insert" menu — routed from MainWindowViewModel to the active tab ----

    /// <summary>Inserts raw text at the selection (Insert Special Character / Insert Clip).</summary>
    public void InsertRawText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SyncModelFromDocument();
        ApplyFormat(CodeInsertOperations.InsertText(_model.Text, _selectionStart, _selectionEnd, value));
    }

    /// <summary>Inserts a ready HTML fragment for a resource (<c>&lt;img&gt;</c>/<c>&lt;audio&gt;</c>/<c>&lt;video&gt;</c>/<c>&lt;a&gt;</c>).</summary>
    public void InsertFileFragment(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        SyncModelFromDocument();
        ApplyFormat(CodeInsertOperations.InsertFileFragment(_model.Text, _selectionStart, _selectionEnd, html));
    }

    /// <summary>Pastes the clip text.</summary>
    public void PasteClipText(string clipText)
    {
        ArgumentNullException.ThrowIfNull(clipText);
        SyncModelFromDocument();
        ApplyFormat(CodeInsertOperations.PasteClipText(_model.Text, _selectionStart, _selectionEnd, clipText));
    }

    /// <summary>Adds/updates the <c>id</c> attribute on the <c>&lt;a&gt;</c> under the caret.</summary>
    public void InsertId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        SyncModelFromDocument();
        ApplyFormat(CodeInsertOperations.InsertId(_model.Text, _selectionStart, _selectionEnd, id));
    }

    /// <summary>Adds/updates the <c>href</c> attribute on the <c>&lt;a&gt;</c> under the caret.</summary>
    public void InsertHyperlink(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        SyncModelFromDocument();
        ApplyFormat(CodeInsertOperations.InsertHyperlink(_model.Text, _selectionStart, _selectionEnd, target));
    }

    private void RunToggle(string element, string? cssProperty, string? cssValue)
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.ToggleInline(
            _model.Text, _selectionStart, _selectionEnd, element, cssProperty, cssValue));
    }

    private void RunStyle(string property, string value)
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.FormatStyle(_model.Text, _selectionStart, _selectionEnd, property, value));
    }

    private void RunList(string element)
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.ApplyList(_model.Text, _selectionStart, _selectionEnd, element));
    }

    private void RunWrap(bool unwrap)
    {
        SyncModelFromDocument();
        ApplyFormat(CodeFormatOperations.WrapInElement(
            _model.Text, _selectionStart, _selectionEnd, "blockquote", unwrap));
    }

    private void ApplyFormat(FormatEdit edit)
    {
        if (!string.IsNullOrEmpty(edit.StatusMessage))
        {
            _statusBar.ShowMessage(edit.StatusMessage!, TimeSpan.FromSeconds(5));
        }

        if (!edit.Changed)
        {
            return;
        }

        ReplaceDocumentText(edit.Text, edit.SelectionStart, edit.SelectionEnd);
    }

    // Replaces the document content with a minimal edit (common prefix/suffix), then sets
    // the selection to [selStart, selEnd). Keeps a single Undo step.
    private void ReplaceDocumentText(string next, int selStart, int selEnd)
    {
        string current = Document.Text;
        if (!string.Equals(current, next, StringComparison.Ordinal))
        {
            int max = Math.Min(current.Length, next.Length);

            int prefix = 0;
            while (prefix < max && current[prefix] == next[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < max - prefix &&
                   current[current.Length - 1 - suffix] == next[next.Length - 1 - suffix])
            {
                suffix++;
            }

            Document.Replace(prefix, current.Length - prefix - suffix, next[prefix..(next.Length - suffix)]);
        }

        SelectionRequested?.Invoke(
            Math.Clamp(selStart, 0, Document.TextLength),
            Math.Clamp(selEnd, 0, Document.TextLength));
    }

    // An edit coming from Find & Replace — we do not want OnDocumentTextChanged to clear
    // "marked text" (the engine itself shifts its end when replacing inside the area).
    private void ApplySearchEdit(string next, int selStart, int selEnd)
    {
        _applyingSearchEdit = true;
        try
        {
            ReplaceDocumentText(next, selStart, selEnd);
        }
        finally
        {
            _applyingSearchEdit = false;
        }

        MarkedTextChanged?.Invoke();
    }

    private static bool SameMergeCandidate(ElementMergeCandidate? a, ElementMergeCandidate? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.ElementName == b.ElementName && a.Count == b.Count && a.FirstOpenTag == b.FirstOpenTag &&
              a.DifferingOpenTags.SequenceEqual(b.DifferingOpenTags) &&
              a.TextBetween.SequenceEqual(b.TextBetween) && a.RemovedNodes.SequenceEqual(b.RemovedNodes);

    private void RefreshFormatState()
    {
        string block = string.Empty;
        bool removeFormatting = false;
        bool removeTagPair = false;
        ElementMergeCandidate? mergeCandidate = null;
        bool insertFile = false;
        bool insertId = false;
        bool insertHyperlink = false;

        if (Syntax == CodeViewSyntax.Html)
        {
            SyncModelFromDocument();
            string text = _model.Text;
            block = CodeFormatOperations.CaretBlockElementName(text, _caretOffset);
            removeFormatting = CodeFormatOperations.RemoveFormattingAllowed(text, _selectionStart, _selectionEnd);
            removeTagPair = _selectionStart == _selectionEnd &&
                CodeFormatOperations.RemoveTagPairAllowed(text, _caretOffset);
            if (_selectionEnd > _selectionStart)
            {
                mergeCandidate = ElementMerger.Analyze(text, _selectionStart, _selectionEnd);
            }

            insertFile = CodeInsertOperations.InsertFileAllowed(text, _selectionStart);
            insertId = CodeInsertOperations.InsertIdAllowed(text, _selectionStart);
            insertHyperlink = CodeInsertOperations.InsertHyperlinkAllowed(text, _selectionStart);
        }

        if (!string.Equals(block, _caretBlockElement, StringComparison.Ordinal))
        {
            _caretBlockElement = block;
            OnPropertyChanged(nameof(CaretBlockElement));
        }

        if (removeFormatting != _removeFormattingEnabled)
        {
            _removeFormattingEnabled = removeFormatting;
            OnPropertyChanged(nameof(RemoveFormattingEnabled));
        }

        if (removeTagPair != _removeTagPairEnabled)
        {
            _removeTagPairEnabled = removeTagPair;
            OnPropertyChanged(nameof(RemoveTagPairEnabled));
        }

        if (!SameMergeCandidate(mergeCandidate, _mergeCandidate))
        {
            _mergeCandidate = mergeCandidate;
            OnPropertyChanged(nameof(MergeCandidate));
        }

        if (insertFile != _insertFileEnabled)
        {
            _insertFileEnabled = insertFile;
            OnPropertyChanged(nameof(InsertFileEnabled));
        }

        if (insertId != _insertIdEnabled)
        {
            _insertIdEnabled = insertId;
            OnPropertyChanged(nameof(InsertIdEnabled));
        }

        if (insertHyperlink != _insertHyperlinkEnabled)
        {
            _insertHyperlinkEnabled = insertHyperlink;
            OnPropertyChanged(nameof(InsertHyperlinkEnabled));
        }
    }

    private void RefreshSecondaryStatus()
    {
        string next = Syntax == CodeViewSyntax.Css
            ? Strings.Format("CodeView_CssRuleCount", _model.GetCssRuleCount())
            : string.Empty;

        if (!string.Equals(next, _secondaryStatus, StringComparison.Ordinal))
        {
            _secondaryStatus = next;
            OnPropertyChanged(nameof(SecondaryStatus));
        }
    }

    private void SyncModelFromDocument() => _model.Text = Document.Text;

    private WellFormedResult? RefreshWellFormed()
    {
        WellFormedResult? result = _model.CheckWellFormed();
        _wellFormedError = result is { IsWellFormed: false } ? result : null;
        _wellFormedWarning = result?.Warning;

        bool? verdict = result?.IsWellFormed;
        if (verdict != _isWellFormed)
        {
            _isWellFormed = verdict;
            OnPropertyChanged(nameof(IsWellFormed));
        }

        OnPropertyChanged(nameof(WellFormedWarning));
        OnPropertyChanged(nameof(WellFormedError));
        return result;
    }

    private void SetModified(bool value)
    {
        if (IsModified == value)
        {
            return;
        }

        IsModified = value;
        RefreshCaption();
    }

    /// <inheritdoc />
    internal override void RefreshCaption() =>
        Title = (IsModified ? "• " : string.Empty) + Tab.Caption;
}

/// <summary>Completions: the replaced fragment starts at <see cref="ReplaceStart"/> and ends at the caret.</summary>
/// <param name="ReplaceStart">Start of the replaced text.</param>
/// <param name="Items">Items, best first.</param>
public sealed record LinkCompletionResult(int ReplaceStart, IReadOnlyList<LinkCompletionItem> Items);
