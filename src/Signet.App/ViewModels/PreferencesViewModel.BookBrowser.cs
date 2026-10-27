using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.MainUI;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Book Browser: which folders are expanded when a book is loaded.</summary>
public sealed partial class PreferencesViewModel : IDisposable
{
    private IReadOnlyList<FolderExpandOption>? _bookBrowserFolders;

    /// <summary>
    /// One checkbox per Book Browser folder (Text, Styles, Images, Fonts, Audio, Video, Misc): whether the folder is
    /// expanded when a book is loaded. Takes effect for the next book loaded, once saved.
    /// </summary>
    public IReadOnlyList<FolderExpandOption> BookBrowserFolders =>
        _bookBrowserFolders ??= Enum.GetValues<OpfModelGroupKind>()
            .Select(kind => new FolderExpandOption(
                kind,
                () => _settings.GetBookBrowserExpandOnLoad(kind),
                value => _settings.SetBookBrowserExpandOnLoad(kind, value)))
            .ToArray();

    private BookBrowserViewModel? _bookBrowserSample;
    private Book? _sampleBook;
    private StatusBarService? _sampleStatusBar;

    /// <summary>
    /// The Book Browser of the preview of the Book Browser pages: the real view model, over a small book made in memory, reading
    /// the look from the settings of this window (the draft), so every change shows in it at once. Made when first asked for.
    /// </summary>
    public BookBrowserViewModel BookBrowserSample
    {
        get
        {
            if (_bookBrowserSample is null)
            {
                _bookBrowserSample = new BookBrowserViewModel(_settings, SampleStatusBar) { IsSample = true };
                _bookBrowserSample.SetBook(SampleBookInMemory);
                foreach (BookBrowserNode folder in _bookBrowserSample.Nodes.Where(n => n.IsFolder))
                {
                    folder.IsExpanded = true;
                }
            }

            return _bookBrowserSample;
        }
    }

    // The status bar and the book the samples of the previews are made over; made when first needed.
    private StatusBarService SampleStatusBar => _sampleStatusBar ??= new StatusBarService();

    private Book SampleBookInMemory => _sampleBook ??= SampleBook.Create();

    /// <summary>Lets go of the samples of the previews (the window closes).</summary>
    public void Dispose()
    {
        _validationSample = null;
        _footnoteSample = null;
        _cleanupSample = null;
        _findReplaceSample = null;
        _findReplaceSampleSettings = null;
        _findUsagesSample?.Dispose();
        _findUsagesSample = null;
        _searchEditorListSample = null;
        _searchEditorEditSample = null;
        _notificationsSample = null;
        _codeEditorSample?.Dispose();
        _codeEditorSample = null;
        _codeEditorSampleTabs?.Dispose();
        _codeEditorSampleTabs = null;
        _startScreenSample?.Dispose();
        _startScreenSample = null;
        _bookBrowserSample?.Dispose();
        _bookBrowserSample = null;
        _sampleBook?.Dispose();
        _sampleBook = null;
        _sampleStatusBar?.Dispose();
        _sampleStatusBar = null;
    }

    /// <summary>Lower bound of <see cref="BookBrowserIndent"/>.</summary>
    public static decimal BookBrowserIndentMin => BookBrowserAppearance.IndentMin;

    /// <summary>Upper bound of <see cref="BookBrowserIndent"/>.</summary>
    public static decimal BookBrowserIndentMax => BookBrowserAppearance.IndentMax;

    /// <summary>Indentation (px) of one level of the Book Browser tree (the files in a folder).</summary>
    [ObservableProperty]
    private int _bookBrowserIndent;

    /// <summary>Whether the names of the files in the Book Browser show their folders (<c>text/part0001.html</c>).</summary>
    [ObservableProperty]
    private bool _bookBrowserShowFolders;

    /// <summary>Upper bound of <see cref="BookBrowserCellMinHeight"/>.</summary>
    public static decimal BookBrowserCellMinHeightMax => BookBrowserAppearance.CellMinHeightMax;

    /// <summary>Upper bound of <see cref="BookBrowserCellPadding"/>.</summary>
    public static decimal BookBrowserCellPaddingMax => BookBrowserAppearance.CellPaddingMax;

    /// <summary>Minimum height (px) of a cell of the Book Browser tree, so of a row; 0 = as high as the content.</summary>
    [ObservableProperty]
    private int _bookBrowserCellMinHeight;

    /// <summary>Vertical padding (px) of a cell of the Book Browser tree.</summary>
    [ObservableProperty]
    private int _bookBrowserCellPadding;

    /// <summary>Typeface of the names and of the Semantics / Properties cells of the tree; empty = the interface font.</summary>
    [ObservableProperty]
    private string _bookBrowserItemsFontFamily = string.Empty;

    /// <summary>Size (px) of the names and of the Semantics / Properties cells; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _bookBrowserItemsFontSize;

    /// <summary>Typeface of the column headers of the tree; empty = the interface font.</summary>
    [ObservableProperty]
    private string _bookBrowserHeaderFontFamily = string.Empty;

    /// <summary>Size (px) of the column headers of the tree; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _bookBrowserHeaderFontSize;

    /// <summary>Weight of the names and of the Semantics / Properties cells; 0 = not set.</summary>
    [ObservableProperty]
    private int _bookBrowserItemsFontWeight;

    /// <summary>Weight of the column headers of the tree; 0 = not set.</summary>
    [ObservableProperty]
    private int _bookBrowserHeaderFontWeight;

    partial void OnBookBrowserItemsFontWeightChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserHeaderFontWeightChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserIndentChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserShowFoldersChanged(bool value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserCellMinHeightChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserCellPaddingChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserItemsFontFamilyChanged(string value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserItemsFontSizeChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserHeaderFontFamilyChanged(string value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserHeaderFontSizeChanged(int value) => PersistBookBrowserAppearance();

    // Reads the look of the tree from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadBookBrowserAppearance()
    {
        BookBrowserAppearance look = _settings.BookBrowserAppearance;
        BookBrowserIndent = look.Indent;
        BookBrowserShowFolders = look.ShowFolders;
        BookBrowserCellMinHeight = look.CellMinHeight;
        BookBrowserCellPadding = look.CellPadding;
        BookBrowserItemsFontFamily = look.ItemsFontFamily;
        BookBrowserItemsFontSize = look.ItemsFontSize;
        BookBrowserHeaderFontFamily = look.HeaderFontFamily;
        BookBrowserHeaderFontSize = look.HeaderFontSize;
        BookBrowserItemsFontWeight = look.ItemsFontWeight;
        BookBrowserHeaderFontWeight = look.HeaderFontWeight;
    }

    private void PersistBookBrowserAppearance() =>
        _settings.BookBrowserAppearance = new BookBrowserAppearance(
            BookBrowserItemsFontFamily, BookBrowserItemsFontSize,
            BookBrowserHeaderFontFamily, BookBrowserHeaderFontSize,
            BookBrowserIndent, BookBrowserCellMinHeight, BookBrowserCellPadding, BookBrowserShowFolders,
            BookBrowserItemsFontWeight, BookBrowserHeaderFontWeight);

    /// <summary>"Choose…" of the font of the names and cells of the Book Browser tree.</summary>
    [RelayCommand]
    private Task ChooseBookBrowserItemsFont() => ChooseBookBrowserFont(headers: false);

    /// <summary>"Choose…" of the font of the column headers of the Book Browser tree.</summary>
    [RelayCommand]
    private Task ChooseBookBrowserHeaderFont() => ChooseBookBrowserFont(headers: true);

    // The font picker, starting from the font in use (the interface font while the setting follows it).
    private async Task ChooseBookBrowserFont(bool headers)
    {
        string family = headers ? BookBrowserHeaderFontFamily : BookBrowserItemsFontFamily;
        int size = headers ? BookBrowserHeaderFontSize : BookBrowserItemsFontSize;
        int weight = headers ? BookBrowserHeaderFontWeight : BookBrowserItemsFontWeight;
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false, weight),
            new FontPickResult(family, size, weight),
            result =>
            {
                if (headers)
                {
                    BookBrowserHeaderFontFamily = result.Family;
                    BookBrowserHeaderFontSize = result.Size;
                    BookBrowserHeaderFontWeight = result.Weight;
                }
                else
                {
                    BookBrowserItemsFontFamily = result.Family;
                    BookBrowserItemsFontSize = result.Size;
                    BookBrowserItemsFontWeight = result.Weight;
                }
            });
    }

    // A font row of a "Fonts" page (Book Browser, Find Usages): typeface (empty = the interface font) and size (0 = the
    // interface size).
    private FontSettingViewModel InterfaceFontRow(
        string key, string labelKey, string previewKey,
        Func<string> family, Action<string> setFamily, Func<int> size, Action<int> setSize,
        Action reset, System.Windows.Input.ICommand choose, Func<int> weight, Action<int> setWeight,
        int? defaultSize = null, string sizeHintKey = "PreferencesWindow_FontSizeZeroAsInterface", int defaultWeight = 0,
        FontInheritance? inheritance = null) =>
        new(
            key,
            Strings.Get(labelKey),
            Strings.Get(previewKey),
            (family, setFamily),
            (size, setSize, 0, BookBrowserAppearance.FontSizeMax),
            (() => family().Length > 0 ? family() : EffectiveUiFontFamily,
             () => size() > 0 ? size() : defaultSize ?? EffectiveUiFontSize),
            reset,
            choose,
            placeholder: () => EffectiveUiFontFamily,
            sizeHint: Strings.Get(sizeHintKey),
            weight: (weight, setWeight),
            effectiveWeight: () => weight() > 0 ? weight() : defaultWeight,
            inheritance: inheritance);
}

/// <summary>A "expand this folder on load" checkbox of the Book Browser preferences.</summary>
public sealed class FolderExpandOption : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    internal FolderExpandOption(OpfModelGroupKind kind, Func<bool> get, Action<bool> set)
    {
        Kind = kind;
        _get = get;
        _set = set;
    }

    /// <summary>The folder.</summary>
    public OpfModelGroupKind Kind { get; }

    /// <summary>The folder name as the Book Browser shows it (Text, Styles, …).</summary>
    public string Label => Kind.ToString();

    /// <summary>Whether the folder is expanded when a book is loaded.</summary>
    public bool IsChecked
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Announces that the value may have changed (e.g. "Restore appearance defaults").</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}
