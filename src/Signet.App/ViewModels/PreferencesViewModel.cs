using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the modal "Preferences" window: the Language, Appearance, General, Keyboard
/// Shortcuts, Preserve Entities and Spellcheck panels.
/// </summary>
/// <remarks>
/// <para><b>Settings apply immediately:</b> there are no OK/Cancel buttons. As with the other application
/// settings (<see cref="ToolbarCustomizeViewModel"/>, the "Auto Spellcheck" toggle,
/// <see cref="ThemeManager"/>), every change is written to <c>SettingsStore</c> at once and the window
/// has only a "Close" button. This is simpler and more consistent with the rest of the application than
/// keeping a separate OK/Cancel state for ~30 fields.</para>
/// <para><b>"Language" panel</b> — the dropdown saves the choice through
/// <see cref="LocalizationManager.Set"/> immediately, and the interface switches live after the
/// window closes (<c>MainWindow</c> calls <see cref="LocalizationManager.ApplySaved"/>).</para>
/// <para><b>The "General" panel has no "Check for updates"</b> — the application has no update
/// mechanism, so a field with no function behind it is omitted.</para>
/// <para><b>UI font</b> (<c>UiFont</c>/<c>UiFontSize</c>) is applied live through
/// <see cref="UiDensityManager.ApplyUiFont"/>; compact mode — at startup. <b>Appearance has no menu icon size choice</b>
/// (<c>MainMenuIconSize</c>) — the setting exists in <see cref="SettingsStore"/>, but has no consumer
/// (no menu icon scaling). The Code View typeface, size and colors are wired in (<c>CodeTabView.ApplyFont</c> /
/// <c>ApplyTextMateTheme</c>), but applied when a tab is opened and when the theme changes, not live
/// after the setting is saved; the Preview fonts have no live consumer yet. For many
/// appearance settings the effect is visible after a restart (see
/// <see cref="RestartNoticeVisible"/>).</para>
/// </remarks>
public sealed partial class PreferencesViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly SpellChecker _spellChecker;
    private readonly ThemeManager _themeManager;
    private readonly LocalizationManager _localizationManager;
    private readonly IconThemeManager _iconThemeManager;
    private readonly KeyboardShortcutManager _shortcuts;
    private readonly UiDensityManager _uiDensity;

    /// <summary>Creates the view model and loads the current settings.</summary>
    public PreferencesViewModel(
        SettingsStore settings,
        SpellChecker spellChecker,
        ThemeManager themeManager,
        LocalizationManager localizationManager,
        IconThemeManager iconThemeManager,
        KeyboardShortcutManager shortcuts,
        AppActionRegistry actions,
        UiDensityManager uiDensity)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));
        _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
        _localizationManager = localizationManager ?? throw new ArgumentNullException(nameof(localizationManager));
        _iconThemeManager = iconThemeManager ?? throw new ArgumentNullException(nameof(iconThemeManager));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _uiDensity = uiDensity ?? throw new ArgumentNullException(nameof(uiDensity));
        ArgumentNullException.ThrowIfNull(actions);

        // ---- Language ----
        _selectedLanguage = AvailableLanguages.First(l => l.Code == _localizationManager.Current);

        // ---- Appearance ----
        _selectedTheme = _themeManager.Current;
        _selectedIconTheme = AvailableIconThemes.First(t => t.Code == _iconThemeManager.Current);
        _customIconFolder = _iconThemeManager.CustomFolder;
        PreviewAppearance previewFonts = _settings.PreviewAppearance;
        _previewFontStandard = previewFonts.FontFamilyStandard;
        _previewFontSerif = previewFonts.FontFamilySerif;
        _previewFontSansSerif = previewFonts.FontFamilySansSerif;
        _previewFontSize = previewFonts.FontSize;

        // ---- Preview: caret position highlight ----
        PreviewHighlight highlight = _settings.PreviewHighlight;
        _previewHighlightStyle = highlight.Style;
        _previewHighlightOpacity = highlight.OpacityPercent;
        _previewHighlightOutlineWidth = highlight.OutlineWidth;
        _previewHighlightAutoHide = highlight.AutoHide;
        _previewHighlightAutoHideDelay = highlight.AutoHideDelayMs;
        PreviewHighlightLightColor = new ColorSettingRow(
            "PreviewHighlightLight", Strings.Get("PreferencesWindow_PreviewHighlightColorLight"), highlight.LightColor,
            _ => PersistPreviewHighlight(), PickColorAsync);
        PreviewHighlightDarkColor = new ColorSettingRow(
            "PreviewHighlightDark", Strings.Get("PreferencesWindow_PreviewHighlightColorDark"), highlight.DarkColor,
            _ => PersistPreviewHighlight(), PickColorAsync);

        SpecialCharacterAppearance specialChar = _settings.SpecialCharacterAppearance;
        _specialCharacterFontFamily = specialChar.FontFamily;
        _specialCharacterFontSize = specialChar.FontSize;

        _extendedHighlighting = _settings.CodeViewExtendedHighlighting;
        CodeViewLight = new CodeViewAppearanceEditor(_settings.CodeViewAppearance, a => _settings.CodeViewAppearance = a, PickFontAsync, PickColorAsync);
        CodeViewDark = new CodeViewAppearanceEditor(_settings.CodeViewDarkAppearance, a => _settings.CodeViewDarkAppearance = a, PickFontAsync, PickColorAsync);

        // ---- General ----
        _defaultVersionIsEpub3 = _settings.DefaultVersion.StartsWith('3');
        _mendOnOpen = (_settings.CleanOn & CleanOn.Open) != 0;
        _mendOnSave = (_settings.CleanOn & CleanOn.Save) != 0;
        _reopenLastFileOnStartup = _settings.ReopenLastFileOnStartup;
        _fileDropZoneEnabled = _settings.FileDropZoneEnabled;
        _autoCloseTags = _settings.CodeViewAutoCloseTags;
        _clipboardHistoryLimit = _settings.ClipboardHistoryLimit;
        _recentFilesLimit = _settings.RecentFilesLimit;
        _previewRefreshDelay = _settings.UiPreviewTimeout;
        _tempFolderHome = _settings.TempFolderHome;
        _externalXEditorPath = _settings.ExternalXEditorPath;
        _cssEpub2ValidationSpec = _settings.CssEpub2ValidationSpec;
        _cssEpub3ValidationSpec = _settings.CssEpub3ValidationSpec;

        // ---- Spellcheck ----
        _spellCheckEnabled = _settings.SpellCheck;
        _spellCheckNumbers = _settings.SpellCheckNumbers;
        AvailableDictionaries = _spellChecker.Dictionaries()
            .Select(name => new DictionaryOption(name, SpellChecker.DisplayName(name)))
            .ToList();
        AvailableSecondaryDictionaries = AvailableDictionaries
            .Append(new DictionaryOption(string.Empty, Strings.Get("PreferencesWindow_NoSecondaryDictionary")))
            .ToList();
        _primaryDictionary = _settings.Dictionary;
        _secondaryDictionary = _settings.SecondaryDictionary;
        RefreshUserDictionaries(_spellChecker.DefaultUserDictionary);

        // ---- Keyboard Shortcuts ----
        Shortcuts = new ObservableCollection<ShortcutRow>(
            actions.Actions
                .OrderBy(a => a.CategoryDisplayName, StringComparer.CurrentCulture)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                // The label comes from the LIVE AppAction.Text, not from the static Descriptor.Text — Clip Bar
                // slots have the name of the assigned clip set there by RefreshClipActions
                // (and when the slot is empty, the default "Clip N" from the catalog).
                .Select(a => new ShortcutRow(
                    a.Id,
                    a.CategoryDisplayName,
                    a.Text.Replace("_", string.Empty, StringComparison.Ordinal),
                    _shortcuts,
                    RefreshConflicts,
                    ToggleShortcutEditor,
                    LabelOfShortcut))
                .ToList());
        ShortcutGroups = Shortcuts
            .GroupBy(r => r.Category, StringComparer.Ordinal)
            .Select(g => new ShortcutGroup(g.Key, g.ToList()))
            .ToList();
        RefreshConflicts();

        // ---- Preserve Entities ----
        PreserveEntities = new ObservableCollection<PreserveEntityRow>(
            _settings.PreserveEntityCodeNames.Select(p => CreatePreserveEntityRow(p.Code, p.Name)));
    }

    // =====================================================================
    //  Language
    // =====================================================================

    /// <summary>UI languages available to choose from.</summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        LocalizationManager.SupportedLanguages.Select(l => new LanguageOption(l.Code, l.DisplayName)).ToList();

    /// <summary>Selected UI language — saved immediately, the interface switches after the window closes.</summary>
    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption value) => _localizationManager.Set(value.Code);

    // =====================================================================
    //  Appearance
    // =====================================================================

    /// <summary>Values to choose from in the theme field.</summary>
    public static IReadOnlyList<ThemePreference> AvailableThemes { get; } =
        Enum.GetValues<ThemePreference>();

    /// <summary>Preferred interface theme — saved and applied immediately.</summary>
    [ObservableProperty]
    private ThemePreference _selectedTheme;

    partial void OnSelectedThemeChanged(ThemePreference value) => _themeManager.Set(value);

    /// <summary>Action icon sets available to choose from.</summary>
    public static IReadOnlyList<IconThemeOption> AvailableIconThemes { get; } =
        IconThemeManager.SupportedThemes
            .Select(t => new IconThemeOption(t.Code, Strings.TryGet("IconTheme_" + t.Code) ?? t.DisplayName))
            .ToList();

    /// <summary>Selected icon set — saved immediately, the effect is visible after a restart.</summary>
    [ObservableProperty]
    private IconThemeOption _selectedIconTheme;

    partial void OnSelectedIconThemeChanged(IconThemeOption value) => _iconThemeManager.Set(value.Code);

    /// <summary>
    /// Folder with the custom icon set (the "Custom" variant) — it should
    /// contain an <c>icons.json</c> file. Saved immediately, the effect is visible after a
    /// restart together with a change of <see cref="SelectedIconTheme"/>.
    /// </summary>
    [ObservableProperty]
    private string _customIconFolder;

    partial void OnCustomIconFolderChanged(string value) => _iconThemeManager.CustomFolder = value;

    /// <summary>
    /// UI font (menus, windows, panels — not the code editor). Empty = the default for the appearance
    /// mode (compact: system, standard: Inter).
    /// </summary>
    public string UiFontFamily => _settings.UiFont;

    /// <summary>UI font size in px (0 = the default for the appearance mode).</summary>
    public int UiFontSize => _settings.UiFontSize;

    /// <summary>The UI font actually in use (chosen or the mode default) — for the field and the picker window.</summary>
    public string EffectiveUiFontFamily => UiFontFamily.Length > 0 ? UiFontFamily : _uiDensity.DefaultFontName;

    private int EffectiveUiFontSize => UiFontSize > 0 ? UiFontSize : (int)_uiDensity.DefaultFontSize;

    /// <summary>UI font description for the Preferences field: "Segoe UI, 12 px" or "Default (Segoe UI, 12 px)".</summary>
    public string UiFontDescription =>
        UiFontFamily.Length == 0 && UiFontSize == 0
            ? Strings.Format("PreferencesWindow_UiFontDefault", EffectiveUiFontFamily, EffectiveUiFontSize)
            : $"{EffectiveUiFontFamily}, {EffectiveUiFontSize} px";

    /// <summary>
    /// "Choose…" next to the UI font (typeface + size). The window starts with the font actually in use
    /// selected, so OK is available right away; the choice takes effect immediately.
    /// </summary>
    [RelayCommand]
    private async Task ChooseUiFont()
    {
        if (await PickFontAsync(new FontPickRequest(EffectiveUiFontFamily, EffectiveUiFontSize, false)) is { } result)
        {
            _settings.UiFont = result.Family;
            _settings.UiFontSize = result.Size;
            _settings.Save();
            NotifyUiFontChanged();
        }
    }

    /// <summary>"Default" — returns (immediately) to the default font for the appearance mode.</summary>
    [RelayCommand]
    private void ResetUiFont()
    {
        _settings.UiFont = string.Empty;
        _settings.UiFontSize = 0;
        _settings.Save();
        NotifyUiFontChanged();
    }

    private void NotifyUiFontChanged()
    {
        _uiDensity.ApplyUiFont(_settings.UiFont, _settings.UiFontSize);
        OnPropertyChanged(nameof(UiFontFamily));
        OnPropertyChanged(nameof(UiFontSize));
        OnPropertyChanged(nameof(EffectiveUiFontFamily));
        OnPropertyChanged(nameof(UiFontDescription));
    }

    /// <summary>
    /// Compact ("Windows-like") interface look — saved immediately, the effect comes after a restart
    /// (see <see cref="UiDensityManager"/>).
    /// </summary>
    public bool CompactUi
    {
        get => _settings.UiCompactDensity;
        set
        {
            if (_settings.UiCompactDensity != value)
            {
                _settings.UiCompactDensity = value;
                _settings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(UiFontDescription));
            }
        }
    }

    /// <summary>Preview font — standard.</summary>
    [ObservableProperty]
    private string _previewFontStandard;

    /// <summary>Preview font — serif.</summary>
    [ObservableProperty]
    private string _previewFontSerif;

    /// <summary>Preview font — sans-serif.</summary>
    [ObservableProperty]
    private string _previewFontSansSerif;

    /// <summary>Base size of the preview font.</summary>
    [ObservableProperty]
    private int _previewFontSize;

    partial void OnPreviewFontStandardChanged(string value) => PersistPreviewAppearance();

    partial void OnPreviewFontSerifChanged(string value) => PersistPreviewAppearance();

    partial void OnPreviewFontSansSerifChanged(string value) => PersistPreviewAppearance();

    partial void OnPreviewFontSizeChanged(int value) => PersistPreviewAppearance();

    private void PersistPreviewAppearance() =>
        _settings.PreviewAppearance = new PreviewAppearance(PreviewFontStandard, PreviewFontSerif, PreviewFontSansSerif, PreviewFontSize);

    // =====================================================================
    //  Preview — caret position highlight (Code View → Preview)
    // =====================================================================

    /// <summary>Values to choose from in the "Highlight style" field.</summary>
    public static IReadOnlyList<PreviewHighlightStyle> AvailablePreviewHighlightStyles { get; } =
        Enum.GetValues<PreviewHighlightStyle>();

    /// <summary>Block background or a border around the block.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewHighlightBackground), nameof(IsPreviewHighlightOutline))]
    private PreviewHighlightStyle _previewHighlightStyle;

    /// <summary>Whether the background is selected (the opacity field is active).</summary>
    public bool IsPreviewHighlightBackground => PreviewHighlightStyle == PreviewHighlightStyle.Background;

    /// <summary>Whether the border is selected (the thickness field is active).</summary>
    public bool IsPreviewHighlightOutline => PreviewHighlightStyle == PreviewHighlightStyle.Outline;

    /// <summary>Highlight color for the application's light theme.</summary>
    public ColorSettingRow PreviewHighlightLightColor { get; }

    /// <summary>Highlight color for the application's dark theme.</summary>
    public ColorSettingRow PreviewHighlightDarkColor { get; }

    /// <summary>Background opacity in percent (5–100; out of range — clamped on save).</summary>
    [ObservableProperty]
    private int _previewHighlightOpacity;

    /// <summary>Border thickness in pixels (1–20; out of range — clamped on save).</summary>
    [ObservableProperty]
    private int _previewHighlightOutlineWidth;

    /// <summary>Whether the highlight disappears on its own after <see cref="PreviewHighlightAutoHideDelay"/>.</summary>
    [ObservableProperty]
    private bool _previewHighlightAutoHide;

    /// <summary>After how many milliseconds the highlight disappears (500–30000; out of range — clamped).</summary>
    [ObservableProperty]
    private int _previewHighlightAutoHideDelay;

    partial void OnPreviewHighlightStyleChanged(PreviewHighlightStyle value) => PersistPreviewHighlight();

    partial void OnPreviewHighlightOpacityChanged(int value) => PersistPreviewHighlight();

    partial void OnPreviewHighlightOutlineWidthChanged(int value) => PersistPreviewHighlight();

    partial void OnPreviewHighlightAutoHideChanged(bool value) => PersistPreviewHighlight();

    partial void OnPreviewHighlightAutoHideDelayChanged(int value) => PersistPreviewHighlight();

    private void PersistPreviewHighlight() =>
        _settings.PreviewHighlight = new PreviewHighlight(
            PreviewHighlightStyle,
            PreviewHighlightLightColor.Value,
            PreviewHighlightDarkColor.Value,
            PreviewHighlightOpacity,
            PreviewHighlightOutlineWidth,
            PreviewHighlightAutoHide,
            PreviewHighlightAutoHideDelay);

    /// <summary>Font of the "Insert Special Character" window.</summary>
    [ObservableProperty]
    private string _specialCharacterFontFamily;

    /// <summary>Font size of the "Insert Special Character" window.</summary>
    [ObservableProperty]
    private int _specialCharacterFontSize;

    partial void OnSpecialCharacterFontFamilyChanged(string value) => PersistSpecialCharacterAppearance();

    partial void OnSpecialCharacterFontSizeChanged(int value) => PersistSpecialCharacterAppearance();

    private void PersistSpecialCharacterAppearance() =>
        _settings.SpecialCharacterAppearance = new SpecialCharacterAppearance(SpecialCharacterFontFamily, SpecialCharacterFontSize);

    /// <summary>
    /// Font picker window — attached by the view (<c>PreferencesWindow</c>). <see langword="null"/>
    /// (e.g. in tests without a UI) = the "Choose…" commands do nothing.
    /// </summary>
    public Func<FontPickRequest, Task<FontPickResult?>>? FontPicker { get; set; }

    private Task<FontPickResult?> PickFontAsync(FontPickRequest request) =>
        FontPicker is { } picker ? picker(request) : Task.FromResult<FontPickResult?>(null);

    /// <summary>
    /// Color picker window (title = the field label, initial color <c>#RRGGBB</c>) — attached by the
    /// view. Returns the chosen color, or <see langword="null"/> after Cancel; a <see langword="null"/>
    /// delegate (tests without a UI) = the pick buttons do nothing.
    /// </summary>
    public Func<string, string, Task<string?>>? ColorPicker { get; set; }

    private Task<string?> PickColorAsync(string title, string initial) =>
        ColorPicker is { } picker ? picker(title, initial) : Task.FromResult<string?>(null);

    /// <summary>"Choose…" next to the standard preview font (typeface only — the size is shared).</summary>
    [RelayCommand]
    private async Task ChoosePreviewFontStandard()
    {
        if (await PickFontAsync(new FontPickRequest(PreviewFontStandard, null, false)) is { } result)
        {
            PreviewFontStandard = result.Family;
        }
    }

    /// <summary>"Choose…" next to the serif preview font.</summary>
    [RelayCommand]
    private async Task ChoosePreviewFontSerif()
    {
        if (await PickFontAsync(new FontPickRequest(PreviewFontSerif, null, false)) is { } result)
        {
            PreviewFontSerif = result.Family;
        }
    }

    /// <summary>"Choose…" next to the sans-serif preview font.</summary>
    [RelayCommand]
    private async Task ChoosePreviewFontSansSerif()
    {
        if (await PickFontAsync(new FontPickRequest(PreviewFontSansSerif, null, false)) is { } result)
        {
            PreviewFontSansSerif = result.Family;
        }
    }

    /// <summary>"Choose…" next to the "Insert Special Character" window font (typeface + size).</summary>
    [RelayCommand]
    private async Task ChooseSpecialCharacterFont()
    {
        if (await PickFontAsync(new FontPickRequest(SpecialCharacterFontFamily, SpecialCharacterFontSize, false)) is { } result)
        {
            SpecialCharacterFontFamily = result.Family;
            SpecialCharacterFontSize = result.Size;
        }
    }

    /// <summary>
    /// Extended Code View highlighting; when disabled the basic highlighting is used.
    /// </summary>
    [ObservableProperty]
    private bool _extendedHighlighting;

    partial void OnExtendedHighlightingChanged(bool value) => _settings.CodeViewExtendedHighlighting = value;

    /// <summary>Code View appearance editor — light theme.</summary>
    public CodeViewAppearanceEditor CodeViewLight { get; }

    /// <summary>Code View appearance editor — dark theme.</summary>
    public CodeViewAppearanceEditor CodeViewDark { get; }

    /// <summary>
    /// Whether to show the notice that a restart is needed to see changes of the Code View and Preview
    /// fonts/colors (there is no live consumer yet). Always visible on this
    /// tab — informational, independent of whether anything was changed.
    /// </summary>
    public bool RestartNoticeVisible => true;

    /// <summary>"Restore Appearance Defaults".</summary>
    [RelayCommand]
    private void RestoreAppearanceDefaults()
    {
        _settings.ClearAppearanceSettings();

        PreviewAppearance previewFonts = PreviewAppearance.Default;
        PreviewFontStandard = previewFonts.FontFamilyStandard;
        PreviewFontSerif = previewFonts.FontFamilySerif;
        PreviewFontSansSerif = previewFonts.FontFamilySansSerif;
        PreviewFontSize = previewFonts.FontSize;

        SpecialCharacterAppearance specialChar = SpecialCharacterAppearance.Default;
        SpecialCharacterFontFamily = specialChar.FontFamily;
        SpecialCharacterFontSize = specialChar.FontSize;

        CodeViewLight.ResetTo(CodeViewAppearance.LightDefault);
        CodeViewDark.ResetTo(CodeViewAppearance.DarkDefault);
    }

    // =====================================================================
    //  General
    // =====================================================================

    /// <summary>The default EPUB version for a new publication is epub3 (otherwise epub2).</summary>
    [ObservableProperty]
    private bool _defaultVersionIsEpub3;

    partial void OnDefaultVersionIsEpub3Changed(bool value) => _settings.DefaultVersion = value ? "3.0" : "2.0";

    /// <summary>Automatic mending when a publication is opened.</summary>
    [ObservableProperty]
    private bool _mendOnOpen;

    /// <summary>Automatic mending when a publication is saved.</summary>
    [ObservableProperty]
    private bool _mendOnSave;

    partial void OnMendOnOpenChanged(bool value) => PersistCleanOn();

    partial void OnMendOnSaveChanged(bool value) => PersistCleanOn();

    private void PersistCleanOn() =>
        _settings.CleanOn = (MendOnOpen ? CleanOn.Open : CleanOn.Never) | (MendOnSave ? CleanOn.Save : CleanOn.Never);

    /// <summary>Automatically open the most recently used publication at startup.</summary>
    [ObservableProperty]
    private bool _reopenLastFileOnStartup;

    partial void OnReopenLastFileOnStartupChanged(bool value) => _settings.ReopenLastFileOnStartup = value;

    /// <summary>Whether the file drop zone is enabled.</summary>
    [ObservableProperty]
    private bool _fileDropZoneEnabled;

    partial void OnFileDropZoneEnabledChanged(bool value) => _settings.FileDropZoneEnabled = value;

    /// <summary>Clipboard history limit (0–20).</summary>
    [ObservableProperty]
    private int _clipboardHistoryLimit;

    partial void OnClipboardHistoryLimitChanged(int value) => _settings.ClipboardHistoryLimit = value;

    /// <summary>How many recent files to show in the File menu (0–20; out of range — clamped).</summary>
    [ObservableProperty]
    private int _recentFilesLimit;

    partial void OnRecentFilesLimitChanged(int value) =>
        _settings.RecentFilesLimit = Math.Clamp(value, 0, SettingsStore.RecentFilesMax);

    /// <summary>
    /// Delay (ms) of the "live" preview refresh after a change in Code View (100–10000; out of
    /// range — clamped).
    /// </summary>
    [ObservableProperty]
    private int _previewRefreshDelay;

    partial void OnPreviewRefreshDelayChanged(int value) =>
        _settings.UiPreviewTimeout = Math.Clamp(value, SettingsStore.UiPreviewTimeoutMin, SettingsStore.UiPreviewTimeoutMax);

    /// <summary>Parent directory of the working directory (empty = the system default).</summary>
    [ObservableProperty]
    private string _tempFolderHome;

    partial void OnTempFolderHomeChanged(string value) => _settings.TempFolderHome = value;

    /// <summary>Path to the external XHTML editor.</summary>
    [ObservableProperty]
    private string _externalXEditorPath;

    partial void OnExternalXEditorPathChanged(string value) => _settings.ExternalXEditorPath = value;

    /// <summary>Available W3C CSS validation profiles.</summary>
    public static IReadOnlyList<string> AvailableCssValidationSpecs { get; } = new[] { "css2", "css21", "css3", "css30" };

    /// <summary>W3C CSS validation profile for EPUB 2.</summary>
    [ObservableProperty]
    private string _cssEpub2ValidationSpec;

    /// <summary>W3C CSS validation profile for EPUB 3.</summary>
    [ObservableProperty]
    private string _cssEpub3ValidationSpec;

    partial void OnCssEpub2ValidationSpecChanged(string value) => _settings.CssEpub2ValidationSpec = value;

    partial void OnCssEpub3ValidationSpecChanged(string value) => _settings.CssEpub3ValidationSpec = value;

    // =====================================================================
    //  Spellcheck
    // =====================================================================

    /// <summary>Automatic closing of tags after typing <c>&lt;/</c>.</summary>
    [ObservableProperty]
    private bool _autoCloseTags;

    partial void OnAutoCloseTagsChanged(bool value) => _settings.CodeViewAutoCloseTags = value;

    /// <summary>Whether automatic spell checking is enabled.</summary>
    [ObservableProperty]
    private bool _spellCheckEnabled;

    partial void OnSpellCheckEnabledChanged(bool value) => _settings.SpellCheck = value;

    /// <summary>Whether to check the spelling of words containing digits.</summary>
    [ObservableProperty]
    private bool _spellCheckNumbers;

    partial void OnSpellCheckNumbersChanged(bool value) => _settings.SpellCheckNumbers = value;

    /// <summary>Available dictionaries (built-in + installed) with readable language names.</summary>
    public IReadOnlyList<DictionaryOption> AvailableDictionaries { get; }

    /// <summary>Dictionaries to choose from as the secondary one — with a "none" item at the end.</summary>
    public IReadOnlyList<DictionaryOption> AvailableSecondaryDictionaries { get; }

    /// <summary>Primary spelling dictionary (file name).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPrimaryDictionary))]
    private string _primaryDictionary;

    /// <summary>Secondary spelling dictionary (file name; empty = none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSecondaryDictionary))]
    private string _secondaryDictionary;

    /// <summary>The list item matching <see cref="PrimaryDictionary"/> (for the ComboBox).</summary>
    public DictionaryOption? SelectedPrimaryDictionary
    {
        get => AvailableDictionaries.FirstOrDefault(d => d.Name == PrimaryDictionary);
        set
        {
            if (value is not null)
            {
                PrimaryDictionary = value.Name;
            }
        }
    }

    /// <summary>The list item matching <see cref="SecondaryDictionary"/> (for the ComboBox).</summary>
    public DictionaryOption? SelectedSecondaryDictionary
    {
        get => AvailableSecondaryDictionaries.FirstOrDefault(d => d.Name == SecondaryDictionary);
        set
        {
            if (value is not null)
            {
                SecondaryDictionary = value.Name;
            }
        }
    }

    partial void OnPrimaryDictionaryChanged(string value)
    {
        if (value.Length > 0)
        {
            _spellChecker.SetPrimaryDictionary(value);
        }
    }

    partial void OnSecondaryDictionaryChanged(string value) => _spellChecker.SetSecondaryDictionary(value);

    /// <summary>
    /// A window with a single text field (title, label, initial value) — attached by the view.
    /// Returns the typed text, or <see langword="null"/> after Cancel; a <see langword="null"/>
    /// delegate (tests without a UI) = commands that require typing text do nothing.
    /// </summary>
    public Func<string, string, string, Task<string?>>? TextPrompt { get; set; }

    /// <summary>Error message (title, body) — attached by the view; <see langword="null"/> = skipped.</summary>
    public Func<string, string, Task>? ErrorMessage { get; set; }

    private Task<string?> AskTextAsync(string titleKey, string promptKey, string initial) =>
        TextPrompt is { } prompt ? prompt(Strings.Get(titleKey), Strings.Get(promptKey), initial) : Task.FromResult<string?>(null);

    private Task ShowErrorAsync(string message) =>
        ErrorMessage is { } show ? show(Strings.Get("PreferencesWindow_ErrorTitle"), message) : Task.CompletedTask;

    /// <summary>User dictionaries found on disk, with an enable toggle.</summary>
    public ObservableCollection<UserDictionaryRow> UserDictionaries { get; } = new();

    /// <summary>
    /// The selected user dictionary — it is also the default dictionary (where "Add to Dictionary"
    /// puts words), and the word list tab shows its content.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultUserDictionaryText))]
    [NotifyCanExecuteChangedFor(nameof(RenameUserDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyUserDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveUserDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUserWordsCommand))]
    private UserDictionaryRow? _selectedUserDictionary;

    /// <summary>The "Default dictionary: …" caption below the list.</summary>
    public string DefaultUserDictionaryText =>
        Strings.Format("PreferencesWindow_DefaultUserDictionary", SelectedUserDictionary?.Name ?? string.Empty);

    /// <summary>Words of the selected user dictionary, sorted.</summary>
    public ObservableCollection<string> UserWords { get; } = new();

    private IReadOnlyList<string> _selectedUserWords = Array.Empty<string>();

    /// <summary>Sets the words selected in the word list (called by the view).</summary>
    public void SetSelectedUserWords(IEnumerable<string> words)
    {
        _selectedUserWords = words?.ToList() ?? new List<string>();
        EditUserWordCommand.NotifyCanExecuteChanged();
        RemoveUserWordsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUserDictionaryChanged(UserDictionaryRow? value)
    {
        if (value is not null)
        {
            _spellChecker.SetDefaultUserDictionary(value.Name);
        }

        LoadUserWords();
    }

    private void RefreshUserDictionaries(string? select)
    {
        UserDictionaries.Clear();
        IReadOnlyList<string> enabled = _settings.EnabledUserDictionaries;
        foreach (string name in _spellChecker.UserDictionaries())
        {
            UserDictionaries.Add(new UserDictionaryRow(name, enabled.Contains(name), PersistEnabledUserDictionaries));
        }

        // The requested dictionary, or the first one when it does not exist.
        SelectedUserDictionary = UserDictionaries.FirstOrDefault(d => d.Name == select) ?? UserDictionaries.FirstOrDefault();
        LoadUserWords();
    }

    private void LoadUserWords()
    {
        UserWords.Clear();
        if (SelectedUserDictionary is { } dictionary)
        {
            foreach (string word in _spellChecker.UserDictionaryWords(dictionary.Name))
            {
                UserWords.Add(word);
            }
        }

        SetSelectedUserWords(Array.Empty<string>());
        RemoveAllUserWordsCommand.NotifyCanExecuteChanged();
    }

    private void PersistEnabledUserDictionaries()
    {
        _settings.EnabledUserDictionaries = UserDictionaries.Where(d => d.IsEnabled).Select(d => d.Name).ToList();
        _spellChecker.Reload();
    }

    private bool HasSelectedUserDictionary() => SelectedUserDictionary is not null;

    /// <summary>"Add" — a new, empty and enabled user dictionary.</summary>
    [RelayCommand]
    private async Task AddUserDictionary()
    {
        string? name = await AskTextAsync("PreferencesWindow_AddDictionaryTitle", "PreferencesWindow_DictionaryNamePrompt", string.Empty);
        if (name is null || !await CheckDictionaryNameAsync(name, null))
        {
            return;
        }

        await RunFileOperationAsync(() => _spellChecker.CreateUserDictionary(name));
        RefreshUserDictionaries(name);
    }

    /// <summary>"Rename" the selected dictionary.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserDictionary))]
    private async Task RenameUserDictionary()
    {
        string oldName = SelectedUserDictionary!.Name;
        string? name = await AskTextAsync("PreferencesWindow_RenameDictionaryTitle", "PreferencesWindow_DictionaryNamePrompt", oldName);
        if (name is null || name == oldName || !await CheckDictionaryNameAsync(name, oldName))
        {
            return;
        }

        bool done = await RunFileOperationAsync(() => _spellChecker.RenameUserDictionary(oldName, name));
        RefreshUserDictionaries(done ? name : oldName);
    }

    /// <summary>"Copy" — a copy of the selected dictionary named <c>…_copy</c>.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserDictionary))]
    private async Task CopyUserDictionary()
    {
        string source = SelectedUserDictionary!.Name;
        string? copy = null;
        await RunFileOperationAsync(() => copy = _spellChecker.CopyUserDictionary(source));
        RefreshUserDictionaries(copy ?? source);
    }

    /// <summary>"Remove" the selected dictionary (except the last one).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserDictionary))]
    private async Task RemoveUserDictionary()
    {
        if (UserDictionaries.Count <= 1)
        {
            await ShowErrorAsync(Strings.Get("PreferencesWindow_CannotRemoveLastDictionary"));
            return;
        }

        string name = SelectedUserDictionary!.Name;
        await RunFileOperationAsync(() => _spellChecker.RemoveUserDictionary(name));
        RefreshUserDictionaries(_spellChecker.DefaultUserDictionary);
    }

    /// <summary>"Add" words (separated by a space, comma or newline).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserDictionary))]
    private async Task AddUserWords()
    {
        string? text = await AskTextAsync("PreferencesWindow_AddWordsTitle", "PreferencesWindow_AddWordsPrompt", string.Empty);
        if (text is null)
        {
            return;
        }

        string[] added = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        await SaveUserWordsAsync(UserWords.Concat(added));
    }

    /// <summary>"Edit" the first selected word.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserWords))]
    private async Task EditUserWord()
    {
        string oldWord = _selectedUserWords[0];
        string? word = await AskTextAsync("PreferencesWindow_EditWordTitle", "PreferencesWindow_EditWordPrompt", oldWord);
        if (word is null || word == oldWord)
        {
            return;
        }

        await SaveUserWordsAsync(UserWords.Select(w => w == oldWord ? word : w));
    }

    /// <summary>"Remove" the selected words.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedUserWords))]
    private async Task RemoveUserWords()
    {
        HashSet<string> removed = new(_selectedUserWords, StringComparer.Ordinal);
        await SaveUserWordsAsync(UserWords.Where(w => !removed.Contains(w)));
    }

    /// <summary>"Remove all" words of the selected dictionary.</summary>
    [RelayCommand(CanExecute = nameof(HasUserWords))]
    private async Task RemoveAllUserWords() => await SaveUserWordsAsync(Array.Empty<string>());

    // Word separators in "Add words": spaces and commas act like newlines.
    private static readonly char[] WordSeparators = { ' ', ',', '\n', '\r', '\t' };

    private bool HasSelectedUserWords() => _selectedUserWords.Count > 0;

    private bool HasUserWords() => UserWords.Count > 0;

    private async Task SaveUserWordsAsync(IEnumerable<string> words)
    {
        if (SelectedUserDictionary is not { } dictionary)
        {
            return;
        }

        List<string> list = words.ToList();
        await RunFileOperationAsync(() => _spellChecker.SetUserDictionaryWords(dictionary.Name, list));
        LoadUserWords();
    }

    private async Task<bool> CheckDictionaryNameAsync(string name, string? currentName)
    {
        switch (_spellChecker.ValidateUserDictionaryName(name, currentName))
        {
            case UserDictionaryNameProblem.None:
                return true;
            case UserDictionaryNameProblem.Empty:
                // An empty name simply does nothing.
                return false;
            case UserDictionaryNameProblem.AlreadyExists:
                await ShowErrorAsync(Strings.Get("PreferencesWindow_DictionaryExists"));
                return false;
            default:
                await ShowErrorAsync(Strings.Get("PreferencesWindow_DictionaryNameInvalid"));
                return false;
        }
    }

    private async Task<bool> RunFileOperationAsync(Action operation)
    {
        try
        {
            operation();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(Strings.Format("PreferencesWindow_DictionaryFileError", ex.Message));
            return false;
        }
    }

    // =====================================================================
    //  Keyboard Shortcuts
    // =====================================================================

    /// <summary>All application actions with their current shortcut (order: category, id).</summary>
    public ObservableCollection<ShortcutRow> Shortcuts { get; }

    /// <summary>
    /// The same rows grouped by category (menu) — collapsible sections of the shortcut editor.
    /// </summary>
    public IReadOnlyList<ShortcutGroup> ShortcutGroups { get; }

    /// <summary>Pairs of actions sharing the same shortcut (text to display; an empty list = no conflicts).</summary>
    public ObservableCollection<string> Conflicts { get; } = new();

    /// <summary>
    /// Filter text of the shortcut list: a row stays when the action name, its category or its current
    /// shortcut contains this text (case-insensitive; spaces are ignored in the shortcut,
    /// so "ctrl + s" finds "Ctrl+S").
    /// </summary>
    [ObservableProperty]
    private string _shortcutFilter = string.Empty;

    /// <summary>Whether to show only actions whose shortcut conflicts with another action.</summary>
    [ObservableProperty]
    private bool _showOnlyShortcutConflicts;

    partial void OnShortcutFilterChanged(string value) => ApplyShortcutFilter();

    partial void OnShowOnlyShortcutConflictsChanged(bool value) => ApplyShortcutFilter();

    /// <summary>"Reset All" — restores all shortcuts to their defaults.</summary>
    [RelayCommand]
    private void ResetAllShortcuts()
    {
        _shortcuts.ResetAll();
        foreach (ShortcutRow row in Shortcuts)
        {
            row.RefreshFromManager();
        }

        RefreshConflicts();
    }

    // Expands the editor of the given row, collapsing the others (only one shortcut is
    // edited at a time); calling it for an expanded row collapses it.
    private void ToggleShortcutEditor(ShortcutRow row)
    {
        bool expand = !row.IsExpanded;
        foreach (ShortcutRow other in Shortcuts)
        {
            if (other.IsExpanded)
            {
                other.CloseEditor();
            }
        }

        if (expand)
        {
            row.OpenEditor();
        }
    }

    private string LabelOfShortcut(string id) =>
        Shortcuts.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal)) is { } row
            ? $"{row.Label} ({row.Category})"
            : id;

    private void RefreshConflicts()
    {
        Conflicts.Clear();
        Dictionary<string, List<string>> conflictsById = new(StringComparer.Ordinal);
        foreach ((string first, string second, string gesture) in _shortcuts.FindConflicts())
        {
            Conflicts.Add($"{LabelOfShortcut(first)} ↔ {LabelOfShortcut(second)}: {gesture}");
            AddConflict(conflictsById, first, second);
            AddConflict(conflictsById, second, first);
        }

        foreach (ShortcutRow row in Shortcuts)
        {
            row.SetConflicts(conflictsById.TryGetValue(row.Id, out List<string>? others)
                ? others.Select(LabelOfShortcut).ToList()
                : Array.Empty<string>());
        }

        if (ShowOnlyShortcutConflicts)
        {
            ApplyShortcutFilter();
        }
    }

    private static void AddConflict(Dictionary<string, List<string>> map, string id, string other)
    {
        if (!map.TryGetValue(id, out List<string>? list))
        {
            list = new List<string>();
            map[id] = list;
        }

        list.Add(other);
    }

    private void ApplyShortcutFilter()
    {
        string needle = ShortcutFilter.Trim();
        bool filtering = needle.Length > 0 || ShowOnlyShortcutConflicts;
        foreach (ShortcutGroup group in ShortcutGroups)
        {
            bool any = false;
            foreach (ShortcutRow row in group.Rows)
            {
                row.IsVisible = row.Matches(needle) && (!ShowOnlyShortcutConflicts || row.HasConflict);
                any |= row.IsVisible;
            }

            group.IsVisible = any;
            if (filtering && any)
            {
                // With an active filter the matches must be visible without manually expanding the sections.
                group.IsExpanded = true;
            }
        }
    }

    // =====================================================================
    //  Preserve Entities
    // =====================================================================

    /// <summary>Characters preserved as entities by Mend/Mend &amp; Prettify.</summary>
    public ObservableCollection<PreserveEntityRow> PreserveEntities { get; }

    /// <summary>Adds a new (empty) row for editing.</summary>
    [RelayCommand]
    private void AddPreserveEntity() => PreserveEntities.Add(CreatePreserveEntityRow(0, string.Empty));

    private PreserveEntityRow CreatePreserveEntityRow(ushort code, string name) =>
        new(code, name, PersistPreserveEntities, RemovePreserveEntity);

    private void RemovePreserveEntity(PreserveEntityRow row)
    {
        PreserveEntities.Remove(row);
        PersistPreserveEntities();
    }

    /// <summary>Saves the current list to <see cref="SettingsStore"/> — called by the rows after editing.</summary>
    private void PersistPreserveEntities()
    {
        _settings.PreserveEntityCodeNames = PreserveEntities
            .Where(p => p.Code > 0 && p.Name.Length > 0)
            .Select(p => ((ushort)p.Code, p.Name))
            .ToList();
    }
}

/// <summary>
/// Editor of a single <see cref="CodeViewAppearance"/> variant (light/dark) — the font + a list
/// of colors as editable rows (label + <c>#RRGGBB</c> value).
/// </summary>
public sealed partial class CodeViewAppearanceEditor : ObservableObject
{
    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();

    private static readonly (string Label, Func<CodeViewAppearance, string> Get, Func<CodeViewAppearance, string, CodeViewAppearance> With)[] ColorFields =
    {
        ("CSS Comment", a => a.CssCommentColor, (a, v) => a with { CssCommentColor = v }),
        ("CSS Property", a => a.CssPropertyColor, (a, v) => a with { CssPropertyColor = v }),
        ("CSS Quote", a => a.CssQuoteColor, (a, v) => a with { CssQuoteColor = v }),
        ("CSS Selector", a => a.CssSelectorColor, (a, v) => a with { CssSelectorColor = v }),
        ("CSS Value", a => a.CssValueColor, (a, v) => a with { CssValueColor = v }),
        ("Line Highlight", a => a.LineHighlightColor, (a, v) => a with { LineHighlightColor = v }),
        ("Line Number Background", a => a.LineNumberBackgroundColor, (a, v) => a with { LineNumberBackgroundColor = v }),
        ("Line Number Foreground", a => a.LineNumberForegroundColor, (a, v) => a with { LineNumberForegroundColor = v }),
        ("Spelling Underline", a => a.SpellingUnderlineColor, (a, v) => a with { SpellingUnderlineColor = v }),
        ("XHTML Attribute Name", a => a.XhtmlAttributeNameColor, (a, v) => a with { XhtmlAttributeNameColor = v }),
        ("XHTML Attribute Value", a => a.XhtmlAttributeValueColor, (a, v) => a with { XhtmlAttributeValueColor = v }),
        ("XHTML CSS", a => a.XhtmlCssColor, (a, v) => a with { XhtmlCssColor = v }),
        ("XHTML CSS Comment", a => a.XhtmlCssCommentColor, (a, v) => a with { XhtmlCssCommentColor = v }),
        ("XHTML DOCTYPE", a => a.XhtmlDoctypeColor, (a, v) => a with { XhtmlDoctypeColor = v }),
        ("XHTML Entity", a => a.XhtmlEntityColor, (a, v) => a with { XhtmlEntityColor = v }),
        ("XHTML HTML", a => a.XhtmlHtmlColor, (a, v) => a with { XhtmlHtmlColor = v }),
        ("XHTML HTML Comment", a => a.XhtmlHtmlCommentColor, (a, v) => a with { XhtmlHtmlCommentColor = v }),
        ("CSS Special Selector", a => a.CssSpecialSelectorColor, (a, v) => a with { CssSpecialSelectorColor = v }),
        ("CSS At-Rule", a => a.CssAtRuleColor, (a, v) => a with { CssAtRuleColor = v }),
        ("CSS Constant", a => a.CssConstantColor, (a, v) => a with { CssConstantColor = v }),
        ("XHTML Namespace Prefix", a => a.XhtmlNamespacePrefixColor, (a, v) => a with { XhtmlNamespacePrefixColor = v }),
        ("Link", a => a.LinkColor, (a, v) => a with { LinkColor = v }),
        ("Error Underline", a => a.ErrorUnderlineColor, (a, v) => a with { ErrorUnderlineColor = v }),
        ("Search Match Background", a => a.SearchMatchBackgroundColor, (a, v) => a with { SearchMatchBackgroundColor = v }),
        ("Selection Background", a => a.SelectionBackgroundColor, (a, v) => a with { SelectionBackgroundColor = v }),
    };

    private readonly Action<CodeViewAppearance> _persist;
    private readonly Func<FontPickRequest, Task<FontPickResult?>> _pickFont;

    /// <summary>Creates an editor bound to a specific persistence property in <see cref="SettingsStore"/>.</summary>
    internal CodeViewAppearanceEditor(
        CodeViewAppearance initial,
        Action<CodeViewAppearance> persist,
        Func<FontPickRequest, Task<FontPickResult?>> pickFont,
        Func<string, string, Task<string?>> pickColor)
    {
        _pickFont = pickFont;
        _fontFamily = initial.FontFamily;
        _fontSize = initial.FontSize;
        Colors = new ObservableCollection<ColorSettingRow>(
            ColorFields.Select(f => new ColorSettingRow(
                f.Label,
                Strings.TryGet("CodeViewColor_" + NonAlphanumeric().Replace(f.Label, string.Empty)) ?? f.Label,
                f.Get(initial),
                _ => Persist(),
                pickColor)));
        _persist = persist;
    }

    /// <summary>Editor font family.</summary>
    [ObservableProperty]
    private string _fontFamily;

    /// <summary>Editor font size.</summary>
    [ObservableProperty]
    private int _fontSize;

    partial void OnFontFamilyChanged(string value) => Persist();

    partial void OnFontSizeChanged(int value) => Persist();

    /// <summary>"Choose…" — the font picker window (typeface + size, fixed-width only by default).</summary>
    [RelayCommand]
    private async Task ChooseFont()
    {
        if (await _pickFont(new FontPickRequest(FontFamily, FontSize, true)) is { } result)
        {
            FontFamily = result.Family;
            FontSize = result.Size;
        }
    }

    /// <summary>Highlight color rows (label + hex value).</summary>
    public ObservableCollection<ColorSettingRow> Colors { get; }

    /// <summary>Restores all fields to the given defaults.</summary>
    public void ResetTo(CodeViewAppearance defaults)
    {
        FontFamily = defaults.FontFamily;
        FontSize = defaults.FontSize;
        foreach (ColorSettingRow row in Colors)
        {
            (string _, Func<CodeViewAppearance, string> get, _) = ColorFields.First(f => f.Label == row.Key);
            row.SetValueSilently(get(defaults));
        }

        Persist();
    }

    private void Persist()
    {
        CodeViewAppearance appearance = new() { FontFamily = FontFamily, FontSize = FontSize };
        foreach (ColorSettingRow row in Colors)
        {
            (string _, _, Func<CodeViewAppearance, string, CodeViewAppearance> with) = ColorFields.First(f => f.Label == row.Key);
            appearance = with(appearance, row.Value);
        }

        _persist(appearance);
    }
}

/// <summary>A single Code View highlight color row — a label + a <c>#RRGGBB</c> value editable in the UI.</summary>
public sealed partial class ColorSettingRow : ObservableObject
{
    private readonly Action<string> _onChanged;
    private readonly Func<string, string, Task<string?>> _pickColor;
    private bool _suppressNotify;

    internal ColorSettingRow(string key, string label, string value, Action<string> onChanged, Func<string, string, Task<string?>> pickColor)
    {
        Key = key;
        Label = label;
        _value = value;
        _onChanged = onChanged;
        _pickColor = pickColor;
    }

    /// <summary>"…" — the color picker window; OK sets the chosen color, Cancel keeps the current one.</summary>
    [RelayCommand]
    private async Task ChooseColor()
    {
        if (await _pickColor(Label, Value) is { } chosen)
        {
            Value = chosen;
        }
    }

    /// <summary>Field identifier — an English name (e.g. "CSS Comment").</summary>
    public string Key { get; }

    /// <summary>Field label in the UI language.</summary>
    public string Label { get; }

    /// <summary>Color value as <c>#RRGGBB</c>.</summary>
    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (!_suppressNotify)
        {
            _onChanged(value);
        }
    }

    internal void SetValueSilently(string value)
    {
        _suppressNotify = true;
        Value = value;
        _suppressNotify = false;
    }
}

/// <summary>A Hunspell dictionary to choose: file name and readable language name (e.g. "English - Great Britain").</summary>
public sealed record DictionaryOption(string Name, string DisplayName);

/// <summary>A single user dictionary found on disk — file name + enable toggle.</summary>
public sealed partial class UserDictionaryRow : ObservableObject
{
    private readonly Action _onChanged;

    internal UserDictionaryRow(string name, bool isEnabled, Action onChanged)
    {
        Name = name;
        _isEnabled = isEnabled;
        _onChanged = onChanged;
    }

    /// <summary>Dictionary file name.</summary>
    public string Name { get; }

    /// <summary>Whether the dictionary is enabled (takes part in spell checking).</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _onChanged();
}

/// <summary>A collapsible section of the shortcut editor — the actions of one category (menu).</summary>
public sealed partial class ShortcutGroup : ObservableObject
{
    internal ShortcutGroup(string category, IReadOnlyList<ShortcutRow> rows)
    {
        Category = category;
        Rows = rows;
    }

    /// <summary>Category name (the section header).</summary>
    public string Category { get; }

    /// <summary>Actions of this category.</summary>
    public IReadOnlyList<ShortcutRow> Rows { get; }

    /// <summary>Whether the section is expanded.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Whether the section has at least one row matching the filter.</summary>
    [ObservableProperty]
    private bool _isVisible = true;
}

/// <summary>
/// A single application action in the keyboard shortcut editor. Collapsed it shows the name, the current
/// shortcut and a possible conflict; expanded (double click) — an editor with "Default" / "Custom"
/// options and key combination capture.
/// </summary>
public sealed partial class ShortcutRow : ObservableObject
{
    private readonly KeyboardShortcutManager _shortcuts;
    private readonly Action _onChanged;
    private readonly Action<ShortcutRow> _toggleEditor;
    private readonly Func<string, string> _labelOf;
    private bool _syncingCustom;

    internal ShortcutRow(
        string id,
        string category,
        string label,
        KeyboardShortcutManager shortcuts,
        Action onChanged,
        Action<ShortcutRow> toggleEditor,
        Func<string, string> labelOf)
    {
        Id = id;
        Category = category;
        Label = label;
        _shortcuts = shortcuts;
        _onChanged = onChanged;
        _toggleEditor = toggleEditor;
        _labelOf = labelOf;
        _gestureText = KeyGestureConversion.ToPortableString(shortcuts.Get(id)?.KeyGesture);
        _isCustom = shortcuts.Get(id)?.IsOverridden ?? false;
    }

    /// <summary>Action identifier.</summary>
    public string Id { get; }

    /// <summary>Category (menu name) — for grouping in the list.</summary>
    public string Category { get; }

    /// <summary>Readable action label.</summary>
    public string Label { get; }

    /// <summary>Current sequence in portable notation (e.g. <c>Ctrl+S</c>); an empty string = no shortcut.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutLine))]
    [NotifyPropertyChangedFor(nameof(CaptureButtonText))]
    private string _gestureText;

    /// <summary>Second line of the collapsed row: "Shortcut: Ctrl+S" / "Shortcut: None".</summary>
    public string ShortcutLine => Strings.Format("PreferencesWindow_ShortcutLine", OrNone(GestureText));

    /// <summary>Label of the "Default: …" option with the action's default sequence.</summary>
    public string DefaultOptionText => Strings.Format(
        "PreferencesWindow_ShortcutDefault",
        OrNone(KeyGestureConversion.ToPortableString(_shortcuts.Get(Id)?.DefaultKeyGesture)));

    /// <summary>Whether the row's editor is expanded.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The "Custom" option is selected (as opposed to "Default").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultChoice))]
    private bool _isCustom;

    /// <summary>The "Default" option is selected (the inverse of <see cref="IsCustom"/>, for radio binding).</summary>
    public bool IsDefaultChoice
    {
        get => !IsCustom;
        set => IsCustom = !value;
    }

    /// <summary>Whether the shortcut button is waiting for a key combination to be pressed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptureButtonText))]
    private bool _isCapturing;

    /// <summary>Capture button content: the current shortcut, "None" or "Press keys…".</summary>
    public string CaptureButtonText => IsCapturing
        ? Strings.Get("PreferencesWindow_ShortcutPressKeys")
        : OrNone(GestureText);

    /// <summary>Editor message (e.g. a rejected shortcut taken by another action), or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorMessage))]
    private string? _editorMessage;

    /// <summary>Whether there is an editor message to show.</summary>
    public bool HasEditorMessage => EditorMessage is not null;

    /// <summary>"Conflicts with: …" — the actions with the same shortcut, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string? _conflictText;

    /// <summary>Whether this action's shortcut conflicts with another action.</summary>
    public bool HasConflict => ConflictText is not null;

    /// <summary>Whether the row matches the current filter.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    partial void OnIsCustomChanged(bool value)
    {
        if (!_syncingCustom && !value)
        {
            // Going back to "Default" = restoring the default sequence.
            ResetToDefault();
        }
    }

    /// <summary>Re-reads the shortcut state from the manager (after Reset/Reset All/a change).</summary>
    public void RefreshFromManager()
    {
        GestureText = KeyGestureConversion.ToPortableString(_shortcuts.Get(Id)?.KeyGesture);
        SetCustomSilently(_shortcuts.Get(Id)?.IsOverridden ?? false);
    }

    /// <summary>
    /// Sets a new sequence (<see langword="null"/> = remove the shortcut). A sequence taken by another
    /// action is rejected — <see cref="EditorMessage"/> then says by which one.
    /// </summary>
    public bool TrySetGesture(KeyGesture? gesture)
    {
        if (gesture is not null && _shortcuts.FindKeyGestureOwner(gesture, Id) is { } owner)
        {
            EditorMessage = Strings.Format(
                "PreferencesWindow_ShortcutInUse", KeyGestureConversion.ToPortableString(gesture), _labelOf(owner));
            return false;
        }

        _shortcuts.SetKeyGesture(Id, gesture);
        EditorMessage = null;
        GestureText = KeyGestureConversion.ToPortableString(_shortcuts.Get(Id)?.KeyGesture);
        // After a manual change we stay on "Custom", even when the result equals the default.
        SetCustomSilently(true);
        _onChanged();
        return true;
    }

    /// <summary>Whether the row matches the filter text (name, category or shortcut).</summary>
    public bool Matches(string needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        if (Label.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || Category.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string compact = needle.Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length > 0 && GestureText.Contains(compact, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts capturing a key combination (a click on the shortcut button).</summary>
    public void BeginCapture()
    {
        if (IsCustom)
        {
            EditorMessage = null;
            IsCapturing = true;
        }
    }

    /// <summary>Ends capturing without changing the shortcut (Esc / focus loss).</summary>
    public void CancelCapture() => IsCapturing = false;

    /// <summary>Accepts the combination pressed in capture mode.</summary>
    public void CompleteCapture(KeyGesture gesture)
    {
        IsCapturing = false;
        TrySetGesture(gesture);
    }

    internal void OpenEditor()
    {
        EditorMessage = null;
        IsExpanded = true;
    }

    internal void CloseEditor()
    {
        IsCapturing = false;
        EditorMessage = null;
        IsExpanded = false;
    }

    internal void SetConflicts(IReadOnlyList<string> otherLabels) =>
        ConflictText = otherLabels.Count == 0
            ? null
            : Strings.Format("PreferencesWindow_ShortcutConflictsWith", string.Join(", ", otherLabels));

    /// <summary>Double click on the row / "Done" — expands or collapses this row's editor.</summary>
    [RelayCommand]
    private void ToggleEditor() => _toggleEditor(this);

    /// <summary>The clear button — removes the shortcut (the "Custom" option with no sequence).</summary>
    [RelayCommand]
    private void ClearGesture() => TrySetGesture(null);

    /// <summary>"Reset" — restores this action's default sequence.</summary>
    [RelayCommand]
    private void ResetToDefault()
    {
        _shortcuts.ResetToDefault(Id);
        EditorMessage = null;
        IsCapturing = false;
        RefreshFromManager();
        _onChanged();
    }

    private void SetCustomSilently(bool value)
    {
        _syncingCustom = true;
        try
        {
            IsCustom = value;
        }
        finally
        {
            _syncingCustom = false;
        }
    }

    private static string OrNone(string gesture) =>
        gesture.Length == 0 ? Strings.Get("PreferencesWindow_ShortcutNone") : gesture;
}

/// <summary>A single "Preserve Entities" entry — a character code (decimal) + the entity notation.</summary>
public sealed partial class PreserveEntityRow : ObservableObject
{
    private readonly Action? _onChanged;
    private readonly Action<PreserveEntityRow>? _removeSelf;

    /// <summary>Creates a row not bound to any persistence (e.g. for tests).</summary>
    public PreserveEntityRow(ushort code, string name)
        : this(code, name, null, null)
    {
    }

    internal PreserveEntityRow(ushort code, string name, Action? onChanged, Action<PreserveEntityRow>? removeSelf)
    {
        _codeText = code == 0 ? string.Empty : code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _name = name;
        _onChanged = onChanged;
        _removeSelf = removeSelf;
    }

    /// <summary>"Remove" — removes this row from the list.</summary>
    [RelayCommand]
    private void Remove() => _removeSelf?.Invoke(this);

    /// <summary>Character code (decimal, e.g. <c>160</c>) as editable text.</summary>
    [ObservableProperty]
    private string _codeText;

    /// <summary>Entity notation (e.g. <c>&amp;#160;</c>).</summary>
    [ObservableProperty]
    private string _name;

    partial void OnCodeTextChanged(string value) => _onChanged?.Invoke();

    partial void OnNameChanged(string value) => _onChanged?.Invoke();

    /// <summary>Character code as a number (<c>0</c> when the text is invalid).</summary>
    public ushort Code => ushort.TryParse(CodeText, out ushort value) ? value : (ushort)0;
}

/// <summary>A single item of the UI language list in the "Language" panel (culture code + native name).</summary>
public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>A single item of the icon set list in the "Appearance" panel (setting code + name).</summary>
public sealed record IconThemeOption(string Code, string DisplayName);
