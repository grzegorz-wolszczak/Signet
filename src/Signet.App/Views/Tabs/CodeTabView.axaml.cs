using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Signet.App.Actions;
using Signet.App.CodeView;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Search;

namespace Signet.App.Views.Tabs;

/// <summary>
/// Code editor tab view: <c>AvaloniaEdit.TextEditor</c> + syntax highlighting, caret position
/// tracking, well-formedness error underline, Ctrl+wheel zoom, "Go To Line".
/// </summary>
public partial class CodeTabView : UserControl
{
    /// <summary>
    /// Editor font size used when the settings hold no sensible value — the same value as in
    /// <c>CodeTabView.axaml</c> (the default Code View font size).
    /// </summary>
    private const double FallbackFontSize = 13.0;

    private readonly WellFormedErrorRenderer _errorRenderer = new();
    private readonly TagPairHighlightRenderer _tagRenderer = new();
    private readonly SearchHighlightRenderer _searchRenderer = new();
    private readonly SpellCheckHighlightRenderer _spellRenderer = new();
    private readonly SyntaxIssueRenderer _issueRenderer = new();
    private readonly ContextMenu _contextMenu = new();

    // Html/Xml/CSS: dedicated line colorizers; JavaScript/JSON: TextMate (created lazily, only
    // when needed). The view may be reused for another tab, so both variants are switched when
    // the DataContext changes.
    private TextMate.Installation? _textMate;
    private SyntaxColorizer? _lineColorizer;

    private CodeTabViewModel? _boundViewModel;
    private CompletionWindow? _completionWindow;

    // A Find Next match that still has to be brought into view after subsequent layout passes
    // (see ScrollPendingMatch); Attempts limits the number of corrections.
    private (int Start, int End, int Attempts)? _pendingMatchScroll;
    private const int MaxMatchScrollAttempts = 8;

    /// <summary>Initializes the view.</summary>
    public CodeTabView()
    {
        InitializeComponent();

        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = false;
        // Addresses in the code are not turned into clickable links — attribute values use the
        // CodeViewAppearance color, without AvaloniaEdit's hyperlink generator underline.
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_tagRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_searchRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_errorRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_spellRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_issueRenderer);
        Editor.TextArea.TextView.AddHandler(TextView.PointerHoverEvent, OnTextViewPointerHover);
        Editor.TextArea.TextView.AddHandler(TextView.PointerHoverStoppedEvent, OnTextViewPointerHoverStopped);
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.TextView.VisualLinesChanged += (_, _) =>
        {
            if (_pendingMatchScroll is not null)
            {
                Dispatcher.UIThread.Post(ScrollPendingMatch, DispatcherPriority.Loaded);
            }
        };
        Editor.TextArea.SelectionChanged += OnSelectionChanged;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.AddHandler(KeyUpEvent, OnTextAreaKeyUp, RoutingStrategies.Bubble);
        Editor.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        Editor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel);
        Editor.DocumentChanged += OnEditorDocumentChanged;

        _contextMenu.Opening += OnContextMenuOpening;
        Editor.ContextMenu = _contextMenu;

        DataContextChanged += OnDataContextChanged;
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyTextMateTheme();
            ApplyFont();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            _boundViewModel.ScrollToOffsetRequested -= OnScrollToOffsetRequested;
            _boundViewModel.SelectionRequested -= OnSelectionRequested;
            _boundViewModel.FocusRequested -= OnFocusRequested;
            _boundViewModel.SearchResultRequested -= OnSearchResultRequested;
            _boundViewModel.SearchHighlightsChanged -= OnSearchHighlightsChanged;
            _boundViewModel.MarkedTextChanged -= OnMarkedTextChanged;
            _boundViewModel.MisspelledWordsChanged -= OnMisspelledWordsChanged;
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.ExternalLinkRequested -= OnExternalLinkRequested;
        }

        _boundViewModel = DataContext as CodeTabViewModel;
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.ScrollToLineRequested += OnScrollToLineRequested;
        _boundViewModel.ScrollToOffsetRequested += OnScrollToOffsetRequested;
        _boundViewModel.SelectionRequested += OnSelectionRequested;
        _boundViewModel.FocusRequested += OnFocusRequested;
        _boundViewModel.SearchResultRequested += OnSearchResultRequested;
        _boundViewModel.SearchHighlightsChanged += OnSearchHighlightsChanged;
        _boundViewModel.MarkedTextChanged += OnMarkedTextChanged;
        _boundViewModel.MisspelledWordsChanged += OnMisspelledWordsChanged;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _boundViewModel.ExternalLinkRequested += OnExternalLinkRequested;
        _boundViewModel.UpdateSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);

        ApplyGrammar(_boundViewModel.Syntax);
        ApplyTextMateTheme();
        ApplyFont();
        UpdateErrorRenderer();
        UpdateTagRenderer();
        UpdateSearchRenderer();
        UpdateSpellRenderer();
    }

    /// <summary>
    /// The <c>Document="{Binding Document}"</c> binding on the <c>TextEditor</c> may resolve
    /// AFTER the <c>DataContextChanged</c> event — especially when Dock re-materializes the view
    /// through <c>DeferredContentControl</c> on a layout change. Once the document arrives, the
    /// highlights skipped in the first pass (well-formedness error, tag pair, Find matches) are
    /// applied again.
    /// </summary>
    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        UpdateErrorRenderer();
        UpdateTagRenderer();
        UpdateSearchRenderer();
        UpdateSpellRenderer();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CodeTabViewModel.WellFormedError):
                UpdateErrorRenderer();
                break;
            case nameof(CodeTabViewModel.TagHighlight):
                UpdateTagRenderer();
                break;
            case nameof(CodeTabViewModel.ZoomFactor):
                ApplyFont();
                break;
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        int offset = Editor.CaretOffset;
        _boundViewModel?.UpdateCaret(
            Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column, offset, CodepointAt(offset));
        _boundViewModel?.UpdateSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
    }

    // After typing "</" the editor closes the nearest open element; after every typed character
    // the link completions are refreshed.
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text == "/" && _boundViewModel?.TryAutoCloseTag(Editor.CaretOffset) is { } caret)
        {
            Editor.CaretOffset = caret;
        }

        UpdateLinkCompletion();
    }

    private void OnTextAreaKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete)
        {
            UpdateLinkCompletion();
        }
    }

    /// <summary>
    /// File name / anchor completion popup — shown when the caret is inside an <c>href</c>/<c>src</c>
    /// value or inside <c>url(</c>, and recomputed after every character (fuzzy filtering is done by
    /// <see cref="CodeTabViewModel.GetLinkCompletions"/>, not by the AvaloniaEdit list).
    /// </summary>
    private void UpdateLinkCompletion()
    {
        if (_boundViewModel?.GetLinkCompletions(Editor.CaretOffset) is not { } result)
        {
            _completionWindow?.Close();
            return;
        }

        if (_completionWindow is null)
        {
            var window = new CompletionWindow(Editor.TextArea) { CloseWhenCaretAtBeginning = false };
            window.CompletionList.IsFiltering = false;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, window))
                {
                    _completionWindow = null;
                }
            };
            _completionWindow = window;
            Fill(window, result);
            window.Show();
        }
        else
        {
            Fill(_completionWindow, result);
        }
    }

    private void Fill(CompletionWindow window, LinkCompletionResult result)
    {
        window.StartOffset = result.ReplaceStart;
        window.EndOffset = Editor.CaretOffset;
        IList<ICompletionData> data = window.CompletionList.CompletionData;
        data.Clear();
        foreach (LinkCompletionItem item in result.Items)
        {
            data.Add(new LinkCompletionData(item));
        }

        window.CompletionList.SelectedItem = data[0];
    }

    private void OnSelectionChanged(object? sender, EventArgs e) =>
        _boundViewModel?.UpdateSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);

    private void OnSearchResultRequested(int start, int end, bool wrapped)
    {
        if (Editor.Document is null)
        {
            return;
        }

        int docLength = Editor.Document.TextLength;
        int clampedStart = Math.Clamp(start, 0, docLength);
        int clampedEnd = Math.Clamp(end, 0, docLength);

        Editor.Select(clampedStart, Math.Max(0, clampedEnd - clampedStart));
        Editor.CaretOffset = clampedEnd;
        Editor.TextArea.Caret.BringCaretToView(); // e.g. horizontal scrolling without word wrap
        _boundViewModel?.UpdateSelection(clampedStart, clampedEnd);
        _ = wrapped;

        _pendingMatchScroll = (clampedStart, clampedEnd, 0);
        ScrollPendingMatch();
    }

    /// <summary>
    /// Brings the match from <see cref="_pendingMatchScroll"/> into view. Positions of visual lines
    /// AvaloniaEdit has not built yet are estimated (one line per paragraph), so after scrolling and
    /// wrapping long paragraphs the match may shift — and a freshly opened tab has no height yet.
    /// The correction is therefore repeated after every line rebuild (<c>VisualLinesChanged</c>)
    /// until the match is visible or the attempt limit is reached; a selection change (e.g. by the
    /// user) stops it immediately.
    /// </summary>
    private void ScrollPendingMatch()
    {
        if (_pendingMatchScroll is not { } pending)
        {
            return;
        }

        bool selectionUnchanged = Editor.Document is { } document &&
                                  document.TextLength >= pending.End &&
                                  Editor.SelectionStart == pending.Start &&
                                  Editor.SelectionLength == pending.End - pending.Start;
        if (!selectionUnchanged || pending.Attempts >= MaxMatchScrollAttempts)
        {
            _pendingMatchScroll = null;
            return;
        }

        if (ScrollMatchIntoView(pending.Start, pending.End) == false && Editor.TextArea.TextView.VisualLinesValid)
        {
            _pendingMatchScroll = null;
            return;
        }

        _pendingMatchScroll = pending with { Attempts = pending.Attempts + 1 };
    }

    /// <summary>
    /// Scrolls so that the match <c>[start, end)</c> is fully visible with a margin of one line
    /// above and below it; otherwise centers it vertically. Computed in visual lines, so it also
    /// works when long paragraphs are wrapped.
    /// </summary>
    /// <returns><c>true</c> — scrolled, <c>false</c> — already visible, <c>null</c> — the view has no height.</returns>
    private bool? ScrollMatchIntoView(int start, int end)
    {
        TextView textView = Editor.TextArea.TextView;
        double viewportHeight = textView.Bounds.Height;
        if (Editor.Document is not { } document || viewportHeight <= 0)
        {
            return null;
        }

        textView.EnsureVisualLines();
        double margin = textView.DefaultLineHeight;
        double top = textView.GetVisualPosition(
            new TextViewPosition(document.GetLocation(start)), VisualYPosition.LineTop).Y;
        double bottom = textView.GetVisualPosition(
            new TextViewPosition(document.GetLocation(end)), VisualYPosition.LineBottom).Y;
        double offset = textView.VerticalOffset;

        if (top - margin >= offset && bottom + margin <= offset + viewportHeight)
        {
            return false;
        }

        // Match taller than the viewport: show its start with a top margin instead of centering.
        double target = bottom - top + (2 * margin) > viewportHeight
            ? top - margin
            : ((top + bottom) / 2) - (viewportHeight / 2);
        // MakeVisible with a viewport-high rectangle sets exactly this offset (the caret scrolls
        // the same way); TextEditor.ScrollToVerticalOffset did not change the scroll position here.
        textView.MakeVisible(new Rect(textView.HorizontalOffset, Math.Max(0, target), 1, viewportHeight));
        return true;
    }

    private void OnSearchHighlightsChanged()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        _searchRenderer.SetMatches(_boundViewModel.SearchHighlights);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void OnMarkedTextChanged() => UpdateSearchRenderer();

    private void UpdateSearchRenderer()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        _searchRenderer.SetMatches(_boundViewModel.SearchHighlights);

        MarkedTextRegion marked = _boundViewModel.Search.Marked;
        _searchRenderer.SetMarked(marked.IsMarked ? (marked.Start, marked.End) : null);

        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void OnMisspelledWordsChanged() => UpdateSpellRenderer();

    private void UpdateSpellRenderer()
    {
        _spellRenderer.SetWords(_boundViewModel?.MisspelledWordHighlights ?? Array.Empty<(int, int)>());
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>
    /// Builds the Code View context menu right before it is shown. Order from the top:
    /// View Image / Open Tab For Image, spelling, Clips / Add To Clips, Toggle Line Wrap Mode,
    /// Go To Link Or Style, Rename Class, Merge Content, Mark Selected Text, Reformat HTML
    /// (or Reformat CSS), then the standard Undo … Select All.
    /// </summary>
    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _contextMenu.Items.Clear();
        if (_boundViewModel is not { } vm)
        {
            e.Cancel = true;
            return;
        }

        List<Control> items = new();
        ICodeTabHost? host = vm.Host;

        // ---- View Image / Open Tab For Image ----
        if (host is not null && vm.ImageBookPathAtCaret() is not null)
        {
            items.Add(Item(Strings.Get("CodeViewMenu_ViewImage"), vm.ViewImageAtCaret));
            if (vm.ImageSourceAtCaret() is not null)
            {
                items.Add(Item(Strings.Get("CodeViewMenu_OpenTabForImage"), vm.OpenImageTabAtCaret));
            }

            items.Add(new Separator());
        }

        // ---- Spelling ----
        if (vm.GetMisspelledWordAtCaret() is { } word)
        {
            IReadOnlyList<string> suggestions = vm.GetSuggestionsFor(word);
            foreach (string suggestion in suggestions)
            {
                string captured = suggestion;
                items.Add(Item(captured, () => vm.ReplaceMisspelledWord(word, captured), literal: true));
            }

            if (suggestions.Count > 0)
            {
                items.Add(new Separator());
            }

            items.Add(Item(Strings.Get("CodeViewMenu_AddToDefaultDictionary"), vm.AddCurrentMisspelledWordToDictionary));
            IReadOnlyList<string> userDictionaries = vm.UserDictionaryNames();
            if (userDictionaries.Count > 0)
            {
                MenuItem addToDictionary = new() { Header = Strings.Get("CodeViewMenu_AddToDictionary") };
                foreach (string dictionaryName in userDictionaries)
                {
                    string captured = dictionaryName;
                    addToDictionary.Items.Add(Item(captured, () => vm.AddCurrentMisspelledWordToDictionary(captured), literal: true));
                }

                items.Add(addToDictionary);
            }

            items.Add(Item(Strings.Get("CodeViewMenu_Ignore"), vm.IgnoreCurrentMisspelledWord));
            items.Add(new Separator());
        }

        // ---- Clips / Add To Clips ----
        if (host is not null)
        {
            MenuItem clips = new() { Header = Strings.Get("CodeViewMenu_Clips") };
            AddClipEntries(clips, host.ClipLibraryRoot, host);
            clips.IsEnabled = clips.Items.Count > 0;
            items.Add(clips);

            MenuItem addToClips = Item(Strings.Get("CodeViewMenu_AddToClips"), () => host.AddToClips(vm.SelectedText));
            addToClips.IsEnabled = vm.HasSelection;
            items.Add(addToClips);
            items.Add(new Separator());
        }

        // ---- Toggle Line Wrap Mode ----
        items.Add(Item(Strings.Get("CodeViewMenu_ToggleLineWrapMode"), vm.ToggleLineWrapMode));
        items.Add(new Separator());

        if (host is not null)
        {
            // ---- Go To Link Or Style ----
            items.Add(Item(Strings.Get("CodeViewMenu_GoToLinkOrStyle"), () => host.ExecuteAction(AppActionIds.GoToLinkOrStyle)));
            items.Add(new Separator());

            // ---- Rename Class — the class under the caret in a class attribute or in a
            // stylesheet / <style> block selector ----
            if (vm.ClassAtCaretForRename() is { } classAtCaret)
            {
                items.Add(Item(Strings.Get("CodeViewMenu_RenameClass"), () => RenameClass(r => new RenameClassViewModel(r, classAtCaret))));
                items.Add(new Separator());
            }
            else if (vm.StyleClassAtCaretForRename() is { } styleClassAtCaret)
            {
                items.Add(Item(Strings.Get("CodeViewMenu_RenameClass"), () => RenameClass(r => new RenameClassViewModel(r, styleClassAtCaret))));
                items.Add(new Separator());
            }

            // ---- Merge Content — always visible, disabled when the selection does not span
            // adjacent elements of the same kind ----
            items.Add(Item(
                vm.MergeContentText,
                () => host.ExecuteAction(AppActionIds.MergeContent),
                enabled: vm.MergeCandidate is not null,
                literal: true));
            items.Add(new Separator());

            // ---- Mark Selected Text / Unmark Marked Text ----
            items.Add(Item(
                Strings.Get(vm.OffersUnmark ? "CodeViewMenu_UnmarkMarkedText" : "CodeViewMenu_MarkSelectedText"),
                () => host.ExecuteAction(AppActionIds.MarkSelection)));
            items.Add(new Separator());

            // ---- Reformat HTML ----
            if (vm.IsHtmlFlow)
            {
                MenuItem reformat = new() { Header = Strings.Get("CodeViewMenu_ReformatHtml") };
                reformat.Items.Add(Item(Strings.Get("CodeViewMenu_Prettify"), () => vm.ReformatHtml(toValid: false), "beautify"));
                reformat.Items.Add(Item(Strings.Get("CodeViewMenu_PrettifyAll"), () => host.ExecuteAction(AppActionIds.MendPrettifyHtml), "beautify"));
                reformat.Items.Add(new Separator());
                reformat.Items.Add(Item(Strings.Get("CodeViewMenu_Mend"), () => vm.ReformatHtml(toValid: true), "html-fix"));
                reformat.Items.Add(Item(Strings.Get("CodeViewMenu_MendAll"), () => host.ExecuteAction(AppActionIds.MendHtml), "html-fix"));
                items.Add(reformat);
                items.Add(new Separator());
            }
        }

        // ---- Reformat CSS ----
        if (vm.IsCss)
        {
            MenuItem reformatCss = new() { Header = Strings.Get("CodeViewMenu_ReformatCss") };
            reformatCss.Items.Add(Item(Strings.Get("CodeViewMenu_CssMultipleLines"), () => vm.ReformatCss(multipleLineFormat: true)));
            reformatCss.Items.Add(Item(Strings.Get("CodeViewMenu_CssSingleLine"), () => vm.ReformatCss(multipleLineFormat: false)));
            items.Add(reformatCss);
            items.Add(new Separator());
        }

        // ---- Standard edit menu ----
        items.Add(Item(Strings.Get("CodeViewMenu_Undo"), vm.Undo, "edit-undo", enabled: vm.CanUndo));
        items.Add(Item(Strings.Get("CodeViewMenu_Redo"), vm.Redo, "edit-redo", enabled: vm.CanRedo));
        items.Add(new Separator());
        items.Add(Item(Strings.Get("CodeViewMenu_Cut"), Editor.Cut, "edit-cut", vm.HasSelection, new KeyGesture(Key.X, KeyModifiers.Control)));
        items.Add(Item(Strings.Get("CodeViewMenu_Copy"), Editor.Copy, "edit-copy", vm.HasSelection, new KeyGesture(Key.C, KeyModifiers.Control)));
        items.Add(Item(Strings.Get("CodeViewMenu_Paste"), Editor.Paste, "edit-paste", gesture: new KeyGesture(Key.V, KeyModifiers.Control)));
        items.Add(Item(Strings.Get("CodeViewMenu_Delete"), Editor.Delete, "edit-delete", vm.HasSelection));
        items.Add(new Separator());
        items.Add(Item(
            Strings.Get("CodeViewMenu_SelectAll"), Editor.SelectAll, "edit-select-all",
            Editor.Document?.TextLength > 0, new KeyGesture(Key.A, KeyModifiers.Control)));

        foreach (Control item in items)
        {
            _contextMenu.Items.Add(item);
        }
    }

    // "Clips" submenu — the clip library tree: groups as submenus, clips as items that paste
    // their text.
    private static void AddClipEntries(MenuItem parent, ClipEditorNode node, ICodeTabHost host)
    {
        foreach (ClipEditorNode child in node.Children)
        {
            if (child.IsGroup)
            {
                MenuItem group = new() { Header = EscapeAccessKeys(child.Name) };
                AddClipEntries(group, child, host);
                parent.Items.Add(group);
            }
            else
            {
                string text = child.Text;
                parent.Items.Add(Item(child.Name, () => host.PasteClip(text), literal: true));
            }
        }
    }

    // "Rename Class…" — dialog with the rename scope; the renamer works on the book's saved texts.
    private async void RenameClass(Func<ClassRenamer, RenameClassViewModel> createViewModel)
    {
        if (_boundViewModel?.Host is not { } host || TopLevel.GetTopLevel(this) is not Window owner
            || host.PrepareClassRename() is not { } renamer)
        {
            return;
        }

        ClassRenameResult? result = await RenameClassWindow.AskAsync(owner, createViewModel(renamer));
        if (result is not null)
        {
            host.ApplyClassRename(result);
        }
    }

    private static MenuItem Item(
        string header,
        Action onClick,
        string? iconKey = null,
        bool enabled = true,
        KeyGesture? gesture = null,
        bool literal = false)
    {
        MenuItem item = new()
        {
            Header = literal ? EscapeAccessKeys(header) : header,
            IsEnabled = enabled,
            InputGesture = gesture,
            Icon = IconFor(iconKey),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    // User-supplied names (clips, dictionaries, suggestions) must not lose "_" to Avalonia's
    // access keys — doubling it displays the underscore literally.
    private static string EscapeAccessKeys(string text) => text.Replace("_", "__", StringComparison.Ordinal);

    private static Image? IconFor(string? iconKey) =>
        iconKey is not null
        && IconKeyToImageConverter.Instance.Convert(iconKey, typeof(IImage), null, CultureInfo.InvariantCulture) is IImage image
            ? new Image { Source = image, Width = 16, Height = 16 }
            : null;

    private void OnSelectionRequested(int start, int end)
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        int length = Math.Max(0, end - start);
        Editor.CaretOffset = Math.Clamp(end, 0, document.TextLength);
        Editor.Select(Math.Clamp(start, 0, document.TextLength), length);
        _boundViewModel?.UpdateSelection(start, end);
        Editor.Focus();
    }

    /// <summary>
    /// Unicode code point of the character under the caret (surrogate pairs handled) or <c>-1</c>
    /// at the end of the document.
    /// </summary>
    private int CodepointAt(int offset)
    {
        if (Editor.Document is not { } doc || offset < 0 || offset >= doc.TextLength)
        {
            return -1;
        }

        char c = doc.GetCharAt(offset);
        if (!char.IsSurrogate(c))
        {
            return c;
        }

        if (char.IsHighSurrogate(c) && offset + 1 < doc.TextLength)
        {
            char low = doc.GetCharAt(offset + 1);
            if (char.IsLowSurrogate(low))
            {
                return char.ConvertToUtf32(c, low);
            }
        }
        else if (char.IsLowSurrogate(c) && offset - 1 >= 0)
        {
            char high = doc.GetCharAt(offset - 1);
            if (char.IsHighSurrogate(high))
            {
                return char.ConvertToUtf32(high, c);
            }
        }

        return -1;
    }

    private void OnScrollToLineRequested(int line)
    {
        if (Editor.Document is not { } document || line < 1 || line > document.LineCount)
        {
            return;
        }

        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.TextArea.Caret.BringCaretToView();
        Editor.ScrollToLine(line);
        Editor.Focus();
    }

    private void OnScrollToOffsetRequested(int offset)
    {
        if (Editor.Document is not { } document || offset < 0 || offset > document.TextLength)
        {
            return;
        }

        Editor.SelectionLength = 0;
        Editor.CaretOffset = offset;
        Editor.TextArea.Caret.BringCaretToView(); // e.g. horizontal scrolling without word wrap
        Editor.Focus();

        // The same mechanism as for Find Next: a tab that was just activated (e.g. by a click in the
        // preview while a CSS tab was active) has no layout yet, so a single ScrollToLine would use
        // stale dimensions and leave the line at the very top. The scroll is corrected after the
        // following layout passes until the line is visible.
        _pendingMatchScroll = (offset, offset, 0);
        ScrollPendingMatch();
    }

    private void OnFocusRequested() => Editor.TextArea.Focus();

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_boundViewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        double step = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _boundViewModel.ZoomFactor = Math.Clamp(_boundViewModel.ZoomFactor * step, 0.5, 4.0);
        e.Handled = true;
    }

    /// <summary>
    /// Ctrl+left click: "Jump to CSS class definition" or opening a link. Computes the offset under
    /// the pointer (without waiting for AvaloniaEdit's internal handling to move the caret — hence
    /// the tunneling-phase registration) and passes it to
    /// <see cref="CodeTabViewModel.RequestLinkOrClassJumpAt"/>. When something was handled, the
    /// click does not move the caret.
    /// </summary>
    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Editor.TextArea.TextView).Properties.IsRightButtonPressed)
        {
            MoveCaretForContextMenu(e);
            return;
        }

        if (_boundViewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (!e.GetCurrentPoint(Editor.TextArea.TextView).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // GetPosition expects document coordinates — including the scroll offset.
        TextView textView = Editor.TextArea.TextView;
        Avalonia.Point position = e.GetPosition(textView) + textView.ScrollOffset;
        AvaloniaEdit.TextViewPosition? textViewPosition = textView.GetPosition(position);
        if (textViewPosition is not { } tvp || Editor.Document is null)
        {
            return;
        }

        int offset = Editor.Document.GetOffset(tvp.Location);
        if (_boundViewModel.RequestLinkOrClassJumpAt(offset))
        {
            e.Handled = true;
        }
    }

    // A right click outside the selection moves the caret to the click position so the context
    // menu (spelling, View Image, Go To Link Or Style) acts on what is under the mouse.
    private void MoveCaretForContextMenu(PointerPressedEventArgs e)
    {
        TextView textView = Editor.TextArea.TextView;
        Avalonia.Point position = e.GetPosition(textView) + textView.ScrollOffset;
        if (textView.GetPosition(position) is not { } tvp || Editor.Document is null)
        {
            return;
        }

        int offset = Editor.Document.GetOffset(tvp.Location);
        int selectionStart = Editor.SelectionStart;
        int selectionEnd = selectionStart + Editor.SelectionLength;
        if (offset < selectionStart || offset > selectionEnd)
        {
            Editor.SelectionLength = 0;
            Editor.CaretOffset = offset;
        }
    }

    private void ApplyGrammar(CodeViewSyntax syntax)
    {
        if (_lineColorizer is not null)
        {
            Editor.TextArea.TextView.LineTransformers.Remove(_lineColorizer);
            _lineColorizer = null;
        }

        CodeTabViewModel? vm = _boundViewModel;
        _lineColorizer = CodeViewTextMate.CreateColorizer(
            syntax, vm?.ExtendedHighlighting ?? false, vm is null ? null : vm.LinkTargetExists, CurrentAppearance());
        _issueRenderer.SetColorizer(_lineColorizer);
        if (_lineColorizer is not null)
        {
            _textMate?.Dispose();
            _textMate = null;
            Editor.TextArea.TextView.LineTransformers.Add(_lineColorizer);
            Editor.TextArea.TextView.Redraw();
            return;
        }

        string? scope = CodeViewTextMate.ScopeFor(syntax);
        if (scope is null)
        {
            _textMate?.Dispose();
            _textMate = null;
            return;
        }

        _textMate ??= Editor.InstallTextMate(CodeViewTextMate.Options);
        _textMate.SetGrammar(scope);
    }

    /// <summary>
    /// Tooltip over a span flagged by extended highlighting — syntax error, link, broken link.
    /// </summary>
    private void OnTextViewPointerHover(object? sender, PointerEventArgs e)
    {
        TextView textView = Editor.TextArea.TextView;
        if (_lineColorizer is null || textView.Document is not { } document ||
            textView.GetPosition(e.GetPosition(textView) + textView.ScrollOffset) is not { } position)
        {
            return;
        }

        DocumentLine line = document.GetLineByNumber(position.Line);
        int column = document.GetOffset(position.Location) - line.Offset;
        SyntaxSpan? hit = _lineColorizer.IssuesOf(line)
            .Where(s => column >= s.Start && column < s.Start + s.Length)
            .Select(s => (SyntaxSpan?)s)
            .LastOrDefault();
        if (hit is not { } span)
        {
            return;
        }

        ToolTip.SetTip(textView, Strings.Get("CodeView_Issue_" + span.Issue));
        ToolTip.SetIsOpen(textView, true);
        e.Handled = true;
    }

    private void OnTextViewPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        TextView textView = Editor.TextArea.TextView;
        ToolTip.SetIsOpen(textView, false);
        ToolTip.SetTip(textView, null);
    }

    // External link from Ctrl+click / F3 — opened in the default browser.
    private void OnExternalLinkRequested(string link)
    {
        string url = link.StartsWith("//", StringComparison.Ordinal) ? "https:" + link : link;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            _ = topLevel.Launcher.LaunchUriAsync(uri);
        }
    }

    private CodeViewAppearance CurrentAppearance() =>
        _boundViewModel?.AppearanceFor(ActualThemeVariant == ThemeVariant.Dark) ?? CodeViewAppearance.LightDefault;

    private void ApplyTextMateTheme()
    {
        _textMate?.SetTheme(CodeViewTextMate.ThemeFor(ActualThemeVariant));
        CodeViewAppearance appearance = CurrentAppearance();
        _lineColorizer?.SetAppearance(appearance);

        // Current line background (line highlight color).
        Editor.TextArea.TextView.CurrentLineBackground =
            new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse(appearance.LineHighlightColor));
        Editor.TextArea.TextView.CurrentLineBorder = null;

        // Background of Find matches and of the selection (= current match) — separate for light and dark themes.
        _searchRenderer.SetMatchBrush(ParseBrush(appearance.SearchMatchBackgroundColor, CodeViewAppearance.LightDefault.SearchMatchBackgroundColor));
        Editor.TextArea.SelectionBrush = ParseBrush(appearance.SelectionBackgroundColor, CodeViewAppearance.LightDefault.SelectionBackgroundColor);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private static Avalonia.Media.Immutable.ImmutableSolidColorBrush ParseBrush(string value, string fallback)
    {
        Avalonia.Media.Color color = Avalonia.Media.Color.TryParse(value, out Avalonia.Media.Color parsed)
            ? parsed
            : Avalonia.Media.Color.Parse(fallback);
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
    }

    /// <summary>
    /// Sets the editor's typeface and font size from the current theme's <see cref="CodeViewAppearance"/>
    /// (Preferences → Appearance → Code View), scaled by the current zoom. Called when a tab is
    /// bound, when the zoom changes and when the theme changes (light and dark have separate
    /// appearance settings).
    /// </summary>
    private void ApplyFont()
    {
        CodeViewAppearance appearance = CurrentAppearance();

        if (!string.IsNullOrWhiteSpace(appearance.FontFamily))
        {
            Editor.FontFamily = new Avalonia.Media.FontFamily(appearance.FontFamily);
        }

        double size = appearance.FontSize > 0 ? appearance.FontSize : FallbackFontSize;
        Editor.FontSize = size * (_boundViewModel?.ZoomFactor ?? 1.0);
    }

    private void UpdateErrorRenderer()
    {
        WellFormedResult? error = _boundViewModel?.WellFormedError;
        if (Editor.Document is not { } document
            || error is null || error.Line < 1 || error.Line > document.LineCount)
        {
            _errorRenderer.Clear();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            return;
        }

        DocumentLine lineObj = document.GetLineByNumber(error.Line);
        int column = Math.Clamp(error.Column, 1, lineObj.Length + 1);
        int offset = lineObj.Offset + column - 1;
        int length = Math.Max(1, lineObj.EndOffset - offset);

        _errorRenderer.SetError(offset, length);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void UpdateTagRenderer()
    {
        _tagRenderer.SetPair(_boundViewModel?.TagHighlight);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }
}
