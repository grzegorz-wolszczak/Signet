using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.MainUI;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Book Browser: which folders are expanded when a book is loaded.</summary>
public sealed partial class PreferencesViewModel
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

    /// <summary>Lower bound of <see cref="BookBrowserIndent"/>.</summary>
    public static decimal BookBrowserIndentMin => BookBrowserAppearance.IndentMin;

    /// <summary>Upper bound of <see cref="BookBrowserIndent"/>.</summary>
    public static decimal BookBrowserIndentMax => BookBrowserAppearance.IndentMax;

    /// <summary>Upper bound of <see cref="BookBrowserRowSpacing"/>.</summary>
    public static decimal BookBrowserRowSpacingMax => BookBrowserAppearance.RowSpacingMax;

    /// <summary>Indentation (px) of one level of the Book Browser tree (the files in a folder).</summary>
    [ObservableProperty]
    private int _bookBrowserIndent;

    /// <summary>Space (px) between two rows of the Book Browser tree.</summary>
    [ObservableProperty]
    private int _bookBrowserRowSpacing;

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

    partial void OnBookBrowserIndentChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserRowSpacingChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserItemsFontFamilyChanged(string value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserItemsFontSizeChanged(int value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserHeaderFontFamilyChanged(string value) => PersistBookBrowserAppearance();

    partial void OnBookBrowserHeaderFontSizeChanged(int value) => PersistBookBrowserAppearance();

    // Reads the look of the tree from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadBookBrowserAppearance()
    {
        BookBrowserAppearance look = _settings.BookBrowserAppearance;
        BookBrowserIndent = look.Indent;
        BookBrowserRowSpacing = look.RowSpacing;
        BookBrowserItemsFontFamily = look.ItemsFontFamily;
        BookBrowserItemsFontSize = look.ItemsFontSize;
        BookBrowserHeaderFontFamily = look.HeaderFontFamily;
        BookBrowserHeaderFontSize = look.HeaderFontSize;
    }

    private void PersistBookBrowserAppearance() =>
        _settings.BookBrowserAppearance = new BookBrowserAppearance(
            BookBrowserItemsFontFamily, BookBrowserItemsFontSize,
            BookBrowserHeaderFontFamily, BookBrowserHeaderFontSize,
            BookBrowserIndent, BookBrowserRowSpacing);

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
        if (await PickFontAsync(new FontPickRequest(
                family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false)) is { } result)
        {
            if (headers)
            {
                BookBrowserHeaderFontFamily = result.Family;
                BookBrowserHeaderFontSize = result.Size;
            }
            else
            {
                BookBrowserItemsFontFamily = result.Family;
                BookBrowserItemsFontSize = result.Size;
            }
        }
    }

    // A font row of the Book Browser "Fonts" page: typeface (empty = the interface font) and size (0 = the interface size).
    private FontSettingViewModel BookBrowserFontRow(
        string key, string labelKey, string previewKey,
        Func<string> family, Action<string> setFamily, Func<int> size, Action<int> setSize,
        Action reset, System.Windows.Input.ICommand choose) =>
        new(
            key,
            Strings.Get(labelKey),
            Strings.Get(previewKey),
            (family, setFamily),
            (size, setSize, 0, BookBrowserAppearance.FontSizeMax),
            (() => family().Length > 0 ? family() : EffectiveUiFontFamily,
             () => size() > 0 ? size() : EffectiveUiFontSize),
            reset,
            choose,
            placeholder: () => EffectiveUiFontFamily,
            sizeHint: Strings.Get("PreferencesWindow_FontSizeZeroAsInterface"));
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
