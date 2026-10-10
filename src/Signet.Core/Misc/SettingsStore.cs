using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Signet.Core.BookManipulation;
using Signet.Core.Search;

namespace Signet.Core.Misc;

/// <summary>
/// Typed access layer for configurable user settings, stored as JSON (most keys live in the
/// <c>user_preferences</c> group).
/// </summary>
/// <remarks>
/// Colors in the appearance structures are stored as <c>#RRGGBB</c> strings (Core does not
/// depend on Avalonia). The store also holds <see cref="ThemePreference"/>, window geometry and
/// the recent files list. The class is not thread-safe.
/// </remarks>
public sealed class SettingsStore
{
    private const string Group = "user_preferences";
    private const string GeometryGroup = "geometry";
    private const string MainWindowGroup = "main_window";
    private const string FindReplaceGroup = "find_replace";

    private const float ZoomNormal = 1.0f;
    private const int ClipboardHistoryMax = 20;
    private const int RecentFilesDefault = 9;

    /// <summary>
    /// Upper bound of <see cref="RecentFilesLimit"/> — and also how many paths
    /// <see cref="RecentFiles"/> keeps, so that lowering the limit does not lose history.
    /// </summary>
    public const int RecentFilesMax = 20;

    /// <summary>Lower bound of <see cref="UiPreviewTimeout"/> (ms).</summary>
    public const int UiPreviewTimeoutMin = 100;

    /// <summary>Upper bound of <see cref="UiPreviewTimeout"/> (ms).</summary>
    public const int UiPreviewTimeoutMax = 10000;

    private const int UiPreviewTimeoutDefault = 1000;

    private readonly SettingsFile _file;

    // The state the draft started from (or was last committed at); null for a store that is not a draft.
    private SettingsFile? _draftBaseline;

    // The SettingChanged keys a draft raised since its baseline — replayed on commit, because a composite setting
    // (e.g. "preview_highlight") is stored under several raw keys but announced under its own name.
    private readonly List<string> _draftRaised = new();

    /// <summary>Creates a store on the default file (<see cref="AppDirectories.SettingsFilePath"/>).</summary>
    public SettingsStore()
        : this(AppDirectories.SettingsFilePath)
    {
    }

    /// <summary>Creates a store on the given JSON file.</summary>
    public SettingsStore(string filePath)
    {
        _file = new SettingsFile(filePath);
    }

    private SettingsStore(SettingsFile draftFile, SettingsFile baseline)
    {
        _file = draftFile;
        _draftBaseline = baseline;
    }

    /// <summary>Raised after every setting change (before it is saved to disk).</summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>Path of the settings file.</summary>
    public string FilePath => _file.Path;

    /// <summary>Saves the current state to the file (atomically).</summary>
    public void Save() => _file.Save();

    /// <summary>Discards unsaved changes and reloads the file.</summary>
    public void Reload() => _file.Reload();

    /// <summary>Whether this store is a draft made by <see cref="CreateDraft"/>.</summary>
    public bool IsDraft => _draftBaseline is not null;

    /// <summary>
    /// Creates a draft: an in-memory copy of the current settings with the same typed API, for an editor with
    /// Save / Cancel (the Preferences window). Changes made to the draft stay in it (its <see cref="Save"/> writes
    /// nothing) until <see cref="CommitDraft"/> copies them here; a draft that is just dropped changes nothing.
    /// </summary>
    public SettingsStore CreateDraft()
    {
        if (IsDraft)
        {
            throw new InvalidOperationException("A draft cannot be made from a draft.");
        }

        return new SettingsStore(_file.CloneDetached(), _file.CloneDetached());
    }

    /// <summary>
    /// Makes the draft's current state its starting point: what was written to it so far is no longer a change (for
    /// an editor that writes its initial values while it loads).
    /// </summary>
    public void RebaseDraft()
    {
        if (!IsDraft)
        {
            throw new InvalidOperationException("Only a draft can be rebased.");
        }

        _draftBaseline = _file.CloneDetached();
        _draftRaised.Clear();
    }

    /// <summary>Whether <paramref name="draft"/> holds changes that <see cref="CommitDraft"/> would copy here.</summary>
    public bool HasDraftChanges(SettingsStore draft) => DraftChanges(draft).Count > 0;

    /// <summary>
    /// Copies the entries changed in <paramref name="draft"/> (since it was created or last committed) into this
    /// store, raises <see cref="SettingChanged"/> for them and returns the raised qualified keys (<c>group/key</c>):
    /// the raw entries copied and the keys the draft announced its changes under. Entries the draft did not change
    /// are left alone, so settings written here meanwhile (window geometry, recent files) survive. Nothing changed —
    /// nothing is raised. The draft stays usable; the caller saves this store.
    /// </summary>
    public IReadOnlyList<string> CommitDraft(SettingsStore draft)
    {
        List<(string Group, string Key)> changes = DraftChanges(draft);
        foreach ((string group, string key) in changes)
        {
            _file.SetRaw(group, key, draft._file.GetRaw(group, key)?.DeepClone());
        }

        List<string> keys = changes.Count == 0
            ? new List<string>()
            : changes.Select(c => $"{c.Group}/{c.Key}").Concat(draft._draftRaised).Distinct(StringComparer.Ordinal).ToList();
        draft._draftBaseline = draft._file.CloneDetached();
        draft._draftRaised.Clear();
        foreach (string key in keys)
        {
            RaiseChangedQualified(key);
        }

        return keys;
    }

    // The entries the draft changed since its baseline that also differ from this store.
    private List<(string Group, string Key)> DraftChanges(SettingsStore draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft._draftBaseline is not { } baseline || IsDraft)
        {
            throw new ArgumentException("Not a draft of a settings store.", nameof(draft));
        }

        return draft._file.Entries().Concat(baseline.Entries()).Distinct()
            .Where(e => !JsonNode.DeepEquals(draft._file.GetRaw(e.Group, e.Key), baseline.GetRaw(e.Group, e.Key))
                && !JsonNode.DeepEquals(draft._file.GetRaw(e.Group, e.Key), _file.GetRaw(e.Group, e.Key)))
            .ToList();
    }

    // ---------------------------------------------------------------- UI --- //

    /// <summary>User interface language. Defaults to English (regardless of the system language).</summary>
    public string UiLanguage
    {
        get => ReadString("ui_language", "en");
        set => Write("ui_language", value);
    }

    /// <summary>Interface font (empty = system default).</summary>
    public string UiFont
    {
        get => ReadString("ui_font", string.Empty);
        set => Write("ui_font", value);
    }

    /// <summary>
    /// Interface font size in px (0 = default for the density mode: 12 compact, 14 standard).
    /// </summary>
    public int UiFontSize
    {
        get => ReadInt("ui_font_size", 0);
        set => Write("ui_font_size", value);
    }

    /// <summary>Original interface font (remembered before it was overridden).</summary>
    public string OriginalUiFont
    {
        get => ReadString("original_ui_font", string.Empty);
        set => Write("original_ui_font", value);
    }

    /// <summary>Icon theme name.</summary>
    public string UiIconTheme
    {
        get => ReadString("ui_icon_theme", "main");
        set => Write("ui_icon_theme", value);
    }

    /// <summary>
    /// Folder with a custom icon set (the "Custom" option in Preferences → Appearance) — it should
    /// contain an <c>Icons.axaml</c> file (an Avalonia ResourceDictionary with the same
    /// <c>x:Key</c> keys as the built-in <c>FluentIcons.axaml</c>/<c>MaterialIcons.axaml</c>).
    /// </summary>
    public string UiCustomIconFolder
    {
        get => ReadString("ui_custom_icon_folder", string.Empty);
        set => Write("ui_custom_icon_folder", value);
    }

    /// <summary>
    /// Delay (ms) of the live preview refresh after a change in Code View (default 1000 ms; a value
    /// outside <c><see cref="UiPreviewTimeoutMin"/>..<see cref="UiPreviewTimeoutMax"/></c> → 1000).
    /// </summary>
    public int UiPreviewTimeout
    {
        get
        {
            int timeout = ReadInt("ui_preview_timeout", UiPreviewTimeoutDefault);
            return timeout is >= UiPreviewTimeoutMin and <= UiPreviewTimeoutMax ? timeout : UiPreviewTimeoutDefault;
        }

        set => Write("ui_preview_timeout", value);
    }

    /// <summary>Whether to highlight the focused control.</summary>
    public bool UiHighlightFocusWidgetEnabled
    {
        get => ReadBool("ui_highlight_focus_widget", false);
        set => Write("ui_highlight_focus_widget", value);
    }

    /// <summary>
    /// Compact interface density: the Fluent theme "Compact" density, a 12 px system font and
    /// tighter menus — closer to native Windows applications. Enabled by default; takes effect
    /// after a restart.
    /// </summary>
    public bool UiCompactDensity
    {
        get => ReadBool("ui_compact_density", true);
        set => Write("ui_compact_density", value);
    }

    /// <summary>Whether to use a double-width text cursor.</summary>
    public bool UiDoubleWidthTextCursor
    {
        get => ReadBool("ui_doublewidth_textcursor", false);
        set => Write("ui_doublewidth_textcursor", value);
    }

    /// <summary>Whether to use the application's own custom dark theme.</summary>
    public bool UiUseCustomSignetDarkTheme
    {
        get => ReadBool("ui_custom_signet_dark_theme", true);
        set => Write("ui_custom_signet_dark_theme", value);
    }

    /// <summary>
    /// Interface theme preference. Defaults to
    /// <see cref="ThemePreference.System"/>.
    /// </summary>
    public ThemePreference ThemePreference
    {
        get => Enum.TryParse(ReadString("ui_theme", nameof(ThemePreference.System)), ignoreCase: true, out ThemePreference value)
            ? value
            : ThemePreference.System;
        set => Write("ui_theme", value.ToString());
    }

    // ------------------------------------------------------------ Books --- //

    /// <summary>Default metadata language of a new publication.</summary>
    public string DefaultMetadataLang
    {
        get => ReadString("default_metadata_lang", "en");
        set => Write("default_metadata_lang", value);
    }

    /// <summary>Default EPUB version of a new publication.</summary>
    public string DefaultVersion
    {
        get => ReadString("default_version", "3.0");
        set => Write("default_version", value);
    }

    /// <summary>Name template used when renaming the selected files in the Book Browser.</summary>
    public string RenameTemplate
    {
        get => ReadString("rename_template", string.Empty);
        set => Write("rename_template", value);
    }

    /// <summary>Path of the external XHTML editor.</summary>
    public string ExternalXEditorPath
    {
        get => ReadString("external_xhtml_editor", string.Empty);
        set => Write("external_xhtml_editor", value);
    }

    /// <summary>
    /// Serialized (Dock.Serializer.SystemTextJson) dock layout of the main window — position,
    /// size and placement of the docked panels, not just their visibility (which is kept
    /// separately via <see cref="GetStringMap"/> under the docks group). Empty when the layout
    /// has not been saved yet (first run).
    /// </summary>
    public string MainWindowDockLayout
    {
        get => ReadString("main_window_dock_layout", string.Empty);
        set => Write("main_window_dock_layout", value);
    }

    /// <summary>
    /// Whether File → Save / Save As refreshes the "Find Usages" panel (when it is shown with a search), as if its
    /// "Refresh" button was pressed. Defaults to <c>true</c>.
    /// </summary>
    public bool FindUsagesRefreshOnSave
    {
        get => ReadBool("find_usages_refresh_on_save", true);
        set => Write("find_usages_refresh_on_save", value);
    }

    /// <summary>Whether the file drop zone is enabled.</summary>
    public bool FileDropZoneEnabled
    {
        get => ReadBool("enable_file_drop_zone", true);
        set => Write("enable_file_drop_zone", value);
    }

    /// <summary>When to run automatic source cleanup.</summary>
    public CleanOn CleanOn
    {
        get => (CleanOn)ReadInt("clean_on", (int)(CleanOn.Open | CleanOn.Save));
        set => Write("clean_on", (int)value);
    }

    /// <summary>
    /// Whether operations that rewrite (X)HTML files ask for confirmation when some of them have no
    /// DOCTYPE (enabled by default). Turned off by "Don't ask again" in that dialog.
    /// </summary>
    public bool WarnMissingDoctype
    {
        get => ReadBool("warn_missing_doctype", true);
        set => Write("warn_missing_doctype", value);
    }

    /// <summary>Whether Prettify adds a DOCTYPE to files that have none (disabled by default).</summary>
    public bool PrettifyAddMissingDoctype
    {
        get => ReadBool("prettify_add_missing_doctype", false);
        set => Write("prettify_add_missing_doctype", value);
    }

    /// <summary>
    /// Whether "Mend Code" of the current file shows the changes it made in a diff window (with "Undo the change")
    /// when it changed the file. Disabled by default.
    /// </summary>
    public bool MendShowDiff
    {
        get => ReadBool("mend_show_diff", false);
        set => Write("mend_show_diff", value);
    }

    /// <summary>Whether Mend adds a DOCTYPE to files that have none (disabled by default).</summary>
    public bool MendAddMissingDoctype
    {
        get => ReadBool("mend_add_missing_doctype", false);
        set => Write("mend_add_missing_doctype", value);
    }

    // ------------------------------------------------------------- Zoom --- //

    /// <summary>Image zoom factor.</summary>
    public float ZoomImage
    {
        get => ReadFloat("zoom_image", ZoomNormal);
        set => Write("zoom_image", value);
    }

    /// <summary>Text view (Code View) zoom factor.</summary>
    public float ZoomText
    {
        get => ReadFloat("zoom_text", ZoomNormal);
        set => Write("zoom_text", value);
    }

    /// <summary>Book View / WebView zoom factor.</summary>
    public float ZoomWeb
    {
        get => ReadFloat("zoom_web", ZoomNormal);
        set => Write("zoom_web", value);
    }

    /// <summary>Preview zoom factor.</summary>
    public float ZoomPreview
    {
        get => ReadFloat("zoom_preview", ZoomNormal);
        set => Write("zoom_preview", value);
    }

    /// <summary>Inspector zoom factor.</summary>
    public float ZoomInspector
    {
        get => ReadFloat("zoom_inspector", ZoomNormal);
        set => Write("zoom_inspector", value);
    }

    // ------------------------------------------------------- Spellcheck --- //

    /// <summary>Name of the primary spelling dictionary.</summary>
    public string Dictionary
    {
        get => ReadString("dictionary_name", "en_US");
        set => Write("dictionary_name", value);
    }

    /// <summary>Name of the secondary (auxiliary) spelling dictionary.</summary>
    public string SecondaryDictionary
    {
        get => ReadString("secondary_dictionary_name", string.Empty);
        set => Write("secondary_dictionary_name", value);
    }

    /// <summary>File name of the (default) user dictionary.</summary>
    public string DefaultUserDictionary
    {
        get => ReadString("user_dictionary_name", "default");
        set => Write("user_dictionary_name", value);
    }

    /// <summary>List of enabled user dictionaries.</summary>
    public IReadOnlyList<string> EnabledUserDictionaries
    {
        get => ReadStringList("enabled_user_dictionaries") ?? new List<string> { DefaultUserDictionary };
        set => Write("enabled_user_dictionaries", value);
    }

    /// <summary>
    /// Whether the debug log is written (Preferences → Debug): what the user does — clicks, menu choices, settings
    /// changed, window and panel operations — goes to the log file. Defaults to <c>false</c>.
    /// </summary>
    public bool DebugLogging
    {
        get => ReadBool("debug_logging", false);
        set => Write("debug_logging", value);
    }

    /// <summary>
    /// Whether Code View wraps long lines to the window width (View → Word Wrap). Defaults to
    /// <c>true</c>.
    /// </summary>
    public bool CodeViewWordWrap
    {
        get => ReadBool("code_view_word_wrap", true);
        set => Write("code_view_word_wrap", value);
    }

    /// <summary>
    /// Whether Code View uses extended highlighting (CSS inside <c>&lt;style&gt;</c>, richer CSS,
    /// errors, links, namespace prefixes, bold/italic). Enabled by default; <c>false</c> uses
    /// the basic highlighting.
    /// </summary>
    public bool CodeViewExtendedHighlighting
    {
        get => ReadBool("code_view_extended_highlighting", true);
        set => Write("code_view_extended_highlighting", value);
    }

    /// <summary>
    /// Whether Code View closes the open tag after <c>&lt;/</c> is typed (enabled by default).
    /// </summary>
    public bool CodeViewAutoCloseTags
    {
        get => ReadBool("code_view_auto_close_tags", true);
        set => Write("code_view_auto_close_tags", value);
    }

    /// <summary>
    /// Whether text pasted into Code View is normalized to Unicode NFC (a letter followed by a combining accent becomes
    /// one precomposed character; enabled by default).
    /// </summary>
    public bool CodeViewPasteNormalizeNfc
    {
        get => ReadBool("code_view_paste_normalize_nfc", true);
        set => Write("code_view_paste_normalize_nfc", value);
    }

    /// <summary>
    /// Whether the "Find Usages" panel groups the usages by file (otherwise a flat list); remembered between
    /// sessions, off by default.
    /// </summary>
    public bool FindUsagesGroupByFile
    {
        get => ReadBool("find_usages_group_by_file", false);
        set => Write("find_usages_group_by_file", value);
    }

    /// <summary>
    /// The optional columns shown in the Book Browser next to the name, comma-separated ids ("ReadingOrder",
    /// "Semantics", "Properties"); empty (only the name) by default.
    /// </summary>
    public string BookBrowserColumns
    {
        get => ReadString("book_browser_columns", string.Empty);
        set => Write("book_browser_columns", value);
    }

    /// <summary>Upper bound of <see cref="CodeViewOpenTagHintDelayMs"/>.</summary>
    public const int OpenTagHintDelayMaxMs = 5000;

    /// <summary>
    /// Whether Code View shows the matching opening tag (as in the source, with its line number) in a tooltip when the
    /// mouse rests on a closing tag. On by default.
    /// </summary>
    public bool CodeViewOpenTagHint
    {
        get => ReadBool("code_view_open_tag_hint", true);
        set => Write("code_view_open_tag_hint", value);
    }

    /// <summary>
    /// Look of the opening tag tooltip (<see cref="CodeViewOpenTagHint"/>): font and colors for both themes. Always
    /// read normalized (<see cref="OpenTagHintAppearance.Normalized"/>).
    /// </summary>
    public OpenTagHintAppearance OpenTagHintAppearance
    {
        get
        {
            OpenTagHintAppearance d = OpenTagHintAppearance.Default;
            return new OpenTagHintAppearance(
                ReadString("open_tag_hint_font_family", d.FontFamily),
                ReadInt("open_tag_hint_font_size", d.FontSize),
                ReadString("open_tag_hint_light_background", d.LightBackground),
                ReadString("open_tag_hint_light_foreground", d.LightForeground),
                ReadString("open_tag_hint_dark_background", d.DarkBackground),
                ReadString("open_tag_hint_dark_foreground", d.DarkForeground)).Normalized();
        }

        set
        {
            OpenTagHintAppearance v = value.Normalized();
            WriteSilent("open_tag_hint_font_family", v.FontFamily);
            WriteSilent("open_tag_hint_font_size", v.FontSize);
            WriteSilent("open_tag_hint_light_background", v.LightBackground);
            WriteSilent("open_tag_hint_light_foreground", v.LightForeground);
            WriteSilent("open_tag_hint_dark_background", v.DarkBackground);
            WriteSilent("open_tag_hint_dark_foreground", v.DarkForeground);
            RaiseChanged("open_tag_hint_appearance");
        }
    }

    /// <summary>
    /// After how many milliseconds of resting on a closing tag the opening tag tooltip appears
    /// (<see cref="CodeViewOpenTagHint"/>); 0–<see cref="OpenTagHintDelayMaxMs"/>, 500 by default.
    /// </summary>
    public int CodeViewOpenTagHintDelayMs
    {
        get => Math.Clamp(ReadInt("code_view_open_tag_hint_delay_ms", 500), 0, OpenTagHintDelayMaxMs);
        set => Write("code_view_open_tag_hint_delay_ms", Math.Clamp(value, 0, OpenTagHintDelayMaxMs));
    }

    /// <summary>Whether automatic spell checking is enabled.</summary>
    public bool SpellCheck
    {
        get => ReadBool("spell_check", false);
        set => Write("spell_check", value);
    }

    /// <summary>Whether to spell check words containing digits.</summary>
    public bool SpellCheckNumbers
    {
        get => ReadBool("spell_check_numbers", false);
        set => Write("spell_check_numbers", value);
    }

    /// <summary>Spellcheck Editor: hide words written in capitals only (e.g. <c>NASA</c>) from the list.</summary>
    public bool SpellcheckEditorHideAllCaps
    {
        get => ReadBool("spellcheck_editor_hide_all_caps", false);
        set => Write("spellcheck_editor_hide_all_caps", value);
    }

    /// <summary>Spellcheck Editor: hide camelCase words (a lowercase letter followed by a capital) from the list.</summary>
    public bool SpellcheckEditorHideCamelCase
    {
        get => ReadBool("spellcheck_editor_hide_camel_case", false);
        set => Write("spellcheck_editor_hide_camel_case", value);
    }

    /// <summary>Spellcheck Editor: hide snake_case words (an underscore between word characters) from the list.</summary>
    public bool SpellcheckEditorHideSnakeCase
    {
        get => ReadBool("spellcheck_editor_hide_snake_case", false);
        set => Write("spellcheck_editor_hide_snake_case", value);
    }

    /// <summary>
    /// Check Book: the rule codes (<c>ValidationResult.Code</c>) whose problems the Validation Results panel leaves out.
    /// </summary>
    public IReadOnlyList<string> CheckBookSkippedRules
    {
        get => (_file.GetRaw(Group, "check_book_skipped_rules") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(Group, "check_book_skipped_rules", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/check_book_skipped_rules");
        }
    }

    // ------------------------------------------------- Preview / WebEng --- //

    /// <summary>Remote resources mode in the preview (0 = off).</summary>
    public int RemoteOn
    {
        get => ReadInt("remote_on", 0);
        set => Write("remote_on", value);
    }

    /// <summary>JavaScript mode in the preview (0 = off).</summary>
    public int JavascriptOn
    {
        get => ReadInt("javascript_on", 0);
        set => Write("javascript_on", value);
    }

    /// <summary>Whether the status bar shows the full path (1 = yes).</summary>
    public int ShowFullPathOn
    {
        get => ReadInt("showfullpath_on", 1);
        set => Write("showfullpath_on", value);
    }

    /// <summary>Whether to disable GPU acceleration in the preview.</summary>
    public bool DisableGpu
    {
        get => ReadBool("disable_gpu", false);
        set => Write("disable_gpu", value);
    }

    /// <summary>Print preview DPI.</summary>
    public int PrintPreviewDpi
    {
        get => ReadInt("print_preview_dpi", 96);
        set => Write("print_preview_dpi", value);
    }

    /// <summary>Print DPI.</summary>
    public int PrintDpi
    {
        get => ReadInt("print_dpi", 300);
        set => Write("print_dpi", value);
    }

    /// <summary>Whether to darken the preview in dark mode (1 = yes).</summary>
    public int PreviewDark
    {
        get => ReadInt("preview_dark_in_dm", 1);
        set => Write("preview_dark_in_dm", value);
    }

    /// <summary>Whether to skip the print preview window.</summary>
    public bool SkipPrintPreview
    {
        get => ReadBool("skipprintpreview", false);
        set => Write("skipprintpreview", value);
    }

    /// <summary>Whether to automatically reopen the most recently used publication on startup.</summary>
    public bool ReopenLastFileOnStartup
    {
        get => ReadBool("reopen_last_file", false);
        set => Write("reopen_last_file", value);
    }

    // ------------------------------------------------------- Validation --- //

    /// <summary>CSS specification for W3C validation of EPUB 2 publications.</summary>
    public string CssEpub2ValidationSpec
    {
        get => ReadString("css_epub2_validation_spec", "css21");
        set => Write("css_epub2_validation_spec", value);
    }

    /// <summary>CSS specification for W3C validation of EPUB 3 publications.</summary>
    public string CssEpub3ValidationSpec
    {
        get => ReadString("css_epub3_validation_spec", "css30");
        set => Write("css_epub3_validation_spec", value);
    }

    // ------------------------------------------------------------ Misc ---- //

    /// <summary>
    /// Parent folder of the working (scratchpad) folder. An empty string = use the
    /// default. If the remembered path does not exist, an empty string is returned.
    /// </summary>
    public string TempFolderHome
    {
        get
        {
            string path = ReadString("temp_folder_path", string.Empty);
            return path.Length > 0 && Directory.Exists(path) ? path : string.Empty;
        }

        set => Write("temp_folder_path", value);
    }

    /// <summary>Preferred tab in the appearance settings window.</summary>
    public int AppearancePrefsTabIndex
    {
        get => ReadInt("appearance_prefs_tab_index", 0);
        set => Write("appearance_prefs_tab_index", value);
    }

    /// <summary>Whether to highlight the open/close tag pair when the caret is inside a tag.</summary>
    public bool HighlightOpenCloseTags
    {
        get => ReadBool("code_view_highlight_open_close_tags", true);
        set => Write("code_view_highlight_open_close_tags", value);
    }

    /// <summary>Main menu icon size (multiplier).</summary>
    public double MainMenuIconSize
    {
        get => ReadDouble("main_menu_icon_size", 1.8);
        set => Write("main_menu_icon_size", value);
    }

    /// <summary>
    /// How many clipboard history entries to keep (0 = disabled; a value outside
    /// <c>0..20</c> → 20).
    /// </summary>
    public int ClipboardHistoryLimit
    {
        get
        {
            int limit = ReadInt("clipboard_history_limit", ClipboardHistoryMax);
            return limit is >= 0 and <= ClipboardHistoryMax ? limit : ClipboardHistoryMax;
        }

        set => Write("clipboard_history_limit", value);
    }

    /// <summary>
    /// How many recently opened publications to show in the File menu (0 = none; a value outside
    /// <c>0..<see cref="RecentFilesMax"/></c> → 9).
    /// </summary>
    public int RecentFilesLimit
    {
        get
        {
            int limit = ReadInt("recent_files_limit", RecentFilesDefault);
            return limit is >= 0 and <= RecentFilesMax ? limit : RecentFilesDefault;
        }

        set => Write("recent_files_limit", value);
    }

    /// <summary>
    /// Whether the Navigate Back / Forward history of a saved book is kept between sessions
    /// (<see cref="MainUI.NavigationHistoryStore"/>). Defaults to <c>false</c> — the history lasts one session.
    /// </summary>
    public bool NavigationHistoryRemember
    {
        get => ReadBool("navigation_history_remember", false);
        set => Write("navigation_history_remember", value);
    }

    /// <summary>
    /// How many days a kept navigation place lives (<see cref="NavigationHistoryRemember"/>); a value outside
    /// <c>1..<see cref="MainUI.NavigationHistoryStore.MaxDays"/></c> → the maximum.
    /// </summary>
    public int NavigationHistoryDays
    {
        get
        {
            int days = ReadInt("navigation_history_days", MainUI.NavigationHistoryStore.MaxDays);
            return days is >= 1 and <= MainUI.NavigationHistoryStore.MaxDays ? days : MainUI.NavigationHistoryStore.MaxDays;
        }

        set => Write("navigation_history_days", value);
    }

    /// <summary>
    /// How many places the Recent Locations popup shows; a value outside
    /// <c>1..<see cref="MainUI.RecentLocations.MaxLimit"/></c> → <see cref="MainUI.RecentLocations.DefaultLimit"/>.
    /// </summary>
    public int RecentLocationsLimit
    {
        get
        {
            int limit = ReadInt("recent_locations_limit", MainUI.RecentLocations.DefaultLimit);
            return limit is >= 1 and <= MainUI.RecentLocations.MaxLimit ? limit : MainUI.RecentLocations.DefaultLimit;
        }

        set => Write("recent_locations_limit", value);
    }

    /// <summary>Whether to handle the AltGr key separately.</summary>
    public bool EnableAltGr
    {
        get => ReadBool("enable_altgr", false);
        set => Write("enable_altgr", value);
    }

    /// <summary>
    /// List of (character code, entity form) pairs preserved when encoding entities.
    /// Defaults to <c>[(160, "&amp;#160;")]</c>.
    /// </summary>
    public IReadOnlyList<(ushort Code, string Name)> PreserveEntityCodeNames
    {
        get => ReadEntityPairs();
        set => WriteEntityPairs(value);
    }

    // -------------------------------------------------------- Appearance -- //

    /// <summary>Preview font settings.</summary>
    public PreviewAppearance PreviewAppearance
    {
        get => new(
            ReadString("preview_font_family_standard", PreviewAppearance.Default.FontFamilyStandard),
            ReadString("preview_font_family_serif", PreviewAppearance.Default.FontFamilySerif),
            ReadString("preview_font_family_sans_serif", PreviewAppearance.Default.FontFamilySansSerif),
            ReadInt("preview_font_size", PreviewAppearance.Default.FontSize));

        set
        {
            WriteSilent("preview_font_family_standard", value.FontFamilyStandard);
            WriteSilent("preview_font_family_serif", value.FontFamilySerif);
            WriteSilent("preview_font_family_sans_serif", value.FontFamilySansSerif);
            WriteSilent("preview_font_size", value.FontSize);
            RaiseChanged("preview_appearance");
        }
    }

    /// <summary>
    /// Highlight of the Code View caret location in the preview (style, colors for the light and
    /// dark themes, opacity, outline, auto-hide). Always read normalized
    /// (<see cref="PreviewHighlight.Normalized"/>).
    /// </summary>
    public PreviewHighlight PreviewHighlight
    {
        get
        {
            PreviewHighlight d = PreviewHighlight.Default;
            PreviewHighlightStyle style = Enum.TryParse(
                ReadString("preview_highlight_style", d.Style.ToString()), ignoreCase: true, out PreviewHighlightStyle parsed)
                ? parsed
                : d.Style;
            return new PreviewHighlight(
                style,
                ReadString("preview_highlight_color_light", d.LightColor),
                ReadString("preview_highlight_color_dark", d.DarkColor),
                ReadInt("preview_highlight_opacity", d.OpacityPercent),
                ReadInt("preview_highlight_outline_width", d.OutlineWidth),
                ReadBool("preview_highlight_auto_hide", d.AutoHide),
                ReadInt("preview_highlight_auto_hide_ms", d.AutoHideDelayMs)).Normalized();
        }

        set
        {
            PreviewHighlight v = value.Normalized();
            WriteSilent("preview_highlight_style", v.Style.ToString());
            WriteSilent("preview_highlight_color_light", v.LightColor);
            WriteSilent("preview_highlight_color_dark", v.DarkColor);
            WriteSilent("preview_highlight_opacity", v.OpacityPercent);
            WriteSilent("preview_highlight_outline_width", v.OutlineWidth);
            _file.SetRaw(Group, "preview_highlight_auto_hide", v.AutoHide);
            WriteSilent("preview_highlight_auto_hide_ms", v.AutoHideDelayMs);
            RaiseChanged("preview_highlight");
        }
    }

    /// <summary>
    /// Appearance of the warnings (font size of the warning texts in dialogs, warning color for the light and dark
    /// themes). Always read normalized (<see cref="WarningAppearance.Normalized"/>).
    /// </summary>
    public WarningAppearance WarningAppearance
    {
        get
        {
            WarningAppearance d = WarningAppearance.Default;
            return new WarningAppearance(
                ReadInt("warning_font_size", d.FontSize),
                ReadString("warning_color_light", d.LightColor),
                ReadString("warning_color_dark", d.DarkColor)).Normalized();
        }

        set
        {
            WarningAppearance v = value.Normalized();
            WriteSilent("warning_font_size", v.FontSize);
            WriteSilent("warning_color_light", v.LightColor);
            WriteSilent("warning_color_dark", v.DarkColor);
            RaiseChanged("warning_appearance");
        }
    }

    /// <summary>
    /// Color of the error notifications for the light and dark themes. Always read normalized
    /// (<see cref="ErrorAppearance.Normalized"/>).
    /// </summary>
    public ErrorAppearance ErrorAppearance
    {
        get
        {
            ErrorAppearance d = ErrorAppearance.Default;
            return new ErrorAppearance(
                ReadString("error_color_light", d.LightColor),
                ReadString("error_color_dark", d.DarkColor)).Normalized();
        }

        set
        {
            ErrorAppearance v = value.Normalized();
            WriteSilent("error_color_light", v.LightColor);
            WriteSilent("error_color_dark", v.DarkColor);
            RaiseChanged("error_appearance");
        }
    }

    /// <summary>The edition page settings (Preferences → Edition page). Unknown values fall back to the defaults.</summary>
    public EditionPageSettings EditionPage
    {
        get
        {
            EditionPageSettings d = EditionPageSettings.Default;
            List<EditionPageField> fields = new();
            if (_file.GetRaw(Group, "edition_page_fields") is JsonArray array)
            {
                foreach (JsonNode? node in array)
                {
                    if (node is JsonObject obj)
                    {
                        fields.Add(new EditionPageField(
                            obj["key"] is JsonValue k && k.TryGetValue(out string? key) ? key : string.Empty,
                            obj["value"] is JsonValue v && v.TryGetValue(out string? value) ? value : string.Empty,
                            obj["show"] is not JsonValue show || !show.TryGetValue(out bool shown) || shown));
                    }
                }
            }

            return new EditionPageSettings(
                ReadBool("edition_page_enabled", d.Enabled),
                Enum.TryParse(ReadString("edition_page_position", d.Position.ToString()), out EditionPagePosition position)
                    && Enum.IsDefined(position) ? position : d.Position,
                fields,
                ReadBool("edition_page_show_program", d.ShowProgram),
                ReadBool("edition_page_show_version", d.ShowVersion),
                ReadBool("edition_page_show_revision", d.ShowRevision),
                ReadBool("edition_page_show_date", d.ShowDate),
                ReadBool("edition_page_add_to_toc", d.AddToToc),
                ReadString("edition_page_title", d.Title),
                Math.Clamp(ReadInt("edition_page_heading_level", d.HeadingLevel), 1, 6));
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _file.SetRaw(Group, "edition_page_enabled", value.Enabled);
            WriteSilent("edition_page_position", value.Position.ToString());
            _file.SetRaw(Group, "edition_page_fields", new JsonArray(value.Fields
                .Select(f => (JsonNode?)new JsonObject { ["key"] = f.Key, ["value"] = f.Value, ["show"] = f.Show })
                .ToArray()));
            _file.SetRaw(Group, "edition_page_show_program", value.ShowProgram);
            _file.SetRaw(Group, "edition_page_show_version", value.ShowVersion);
            _file.SetRaw(Group, "edition_page_show_revision", value.ShowRevision);
            _file.SetRaw(Group, "edition_page_show_date", value.ShowDate);
            _file.SetRaw(Group, "edition_page_add_to_toc", value.AddToToc);
            WriteSilent("edition_page_title", value.Title);
            WriteSilent("edition_page_heading_level", Math.Clamp(value.HeadingLevel, 1, 6));
            RaiseChanged("edition_page");
        }
    }

    /// <summary>Code editor appearance settings — light theme.</summary>
    public CodeViewAppearance CodeViewAppearance
    {
        get => ReadCodeView("code_view_", CodeViewAppearance.LightDefault);
        set
        {
            WriteCodeView("code_view_", value);
            RaiseChanged("code_view_appearance");
        }
    }

    /// <summary>Code editor appearance settings — dark theme.</summary>
    public CodeViewAppearance CodeViewDarkAppearance
    {
        get => ReadCodeView("cv_dark_", CodeViewAppearance.DarkDefault);
        set
        {
            WriteCodeView("cv_dark_", value);
            RaiseChanged("cv_dark_appearance");
        }
    }

    /// <summary>Font settings of the "Insert Special Character" window.</summary>
    public SpecialCharacterAppearance SpecialCharacterAppearance
    {
        get => new(
            ReadString("special_character_font_family", SpecialCharacterAppearance.Default.FontFamily),
            ReadInt("special_character_font_size", SpecialCharacterAppearance.Default.FontSize));

        set
        {
            WriteSilent("special_character_font_family", value.FontFamily);
            WriteSilent("special_character_font_size", value.FontSize);
            RaiseChanged("special_character_appearance");
        }
    }

    /// <summary>
    /// Whether the search box of the "Insert Special Character" window searches all characters (default)
    /// or only the rows of the selected category.
    /// </summary>
    public bool SpecialCharacterSearchAll
    {
        get => ReadBool("special_character_search_all", true);
        set => Write("special_character_search_all", value);
    }

    /// <summary>
    /// Whether a double-click in the "Insert Special Character" table inserts the content of the clicked cell
    /// (character, entity, code, hexadecimal or decimal reference) instead of always the character (default).
    /// </summary>
    public bool SpecialCharacterDoubleClickInsertsCell
    {
        get => ReadBool("special_character_double_click_inserts_cell", false);
        set => Write("special_character_double_click_inserts_cell", value);
    }

    /// <summary>
    /// Resets all appearance settings (Preview / Code View / Special Characters)
    /// to their default values.
    /// </summary>
    public void ClearAppearanceSettings()
    {
        foreach (string key in AppearanceKeys())
        {
            _file.Remove(Group, key);
        }

        RaiseChanged("appearance_settings_cleared");
    }

    // --------------------------------------------------- Window / recent - //

    /// <summary>
    /// The page of the Preferences window that was open when it was last closed (its index), so the window opens on it
    /// again. Window state, not a preference — written outside the Save / Apply draft.
    /// </summary>
    public int PreferencesPageIndex
    {
        get => Math.Max(0, ReadIntFrom(MainWindowGroup, "preferences_page", 0));
        set => _file.SetRaw(MainWindowGroup, "preferences_page", Math.Max(0, value));
    }

    /// <summary>Remembered geometry of the window with the given name (<see langword="null"/> when none).</summary>
    public WindowGeometry? GetWindowGeometry(string windowName)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowName);
        if (_file.GetRaw(GeometryGroup, windowName) is not JsonObject obj)
        {
            return null;
        }

        return new WindowGeometry(
            ReadGeometryNumber(obj["x"]),
            ReadGeometryNumber(obj["y"]),
            ReadGeometryNumber(obj["width"]),
            ReadGeometryNumber(obj["height"]),
            obj["maximized"] is JsonValue maximized && maximized.TryGetValue(out bool isMaximized) && isMaximized,
            obj["fullscreen"] is JsonValue fullScreen && fullScreen.TryGetValue(out bool isFullScreen) && isFullScreen);
    }

    // A number of the geometry: an int when it was set in this session (SetWindowGeometry stores the values as int
    // nodes, which GetValue<double> refuses), or any JSON number when read from the file. 0 when missing/not a number.
    private static int ReadGeometryNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue(out int intValue))
        {
            return intValue;
        }

        return value.TryGetValue(out double doubleValue) ? (int)doubleValue : 0;
    }

    /// <summary>Saves the geometry of the window with the given name.</summary>
    public void SetWindowGeometry(string windowName, WindowGeometry geometry)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowName);
        JsonObject obj = new()
        {
            ["x"] = geometry.X,
            ["y"] = geometry.Y,
            ["width"] = geometry.Width,
            ["height"] = geometry.Height,
            ["maximized"] = geometry.Maximized,
            ["fullscreen"] = geometry.FullScreen,
        };
        _file.SetRaw(GeometryGroup, windowName, obj);
        RaiseChangedQualified($"{GeometryGroup}/{windowName}");
    }

    /// <summary>Last folder used to open/save files.</summary>
    public string LastFolderOpen
    {
        get => ReadStringFrom(MainWindowGroup, "lastfolderopen", string.Empty);
        set
        {
            _file.SetRaw(MainWindowGroup, "lastfolderopen", value);
            RaiseChangedQualified($"{MainWindowGroup}/lastfolderopen");
        }
    }

    /// <summary>List of recently opened publications (newest first).</summary>
    public IReadOnlyList<string> RecentFiles
    {
        get => (_file.GetRaw(MainWindowGroup, "recentfiles") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(MainWindowGroup, "recentfiles", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{MainWindowGroup}/recentfiles");
        }
    }

    /// <summary>
    /// Bookpaths of the tabs open when the application was closed (in display order) —
    /// restored when the same publication is reopened.
    /// </summary>
    public IReadOnlyList<string> SessionOpenTabs
    {
        get => (_file.GetRaw(MainWindowGroup, "opentabs") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(MainWindowGroup, "opentabs", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{MainWindowGroup}/opentabs");
        }
    }

    /// <summary>Bookpath of the tab active when the application was closed (empty when there was none).</summary>
    public string SessionActiveTab
    {
        get => ReadStringFrom(MainWindowGroup, "activetab", string.Empty);
        set
        {
            _file.SetRaw(MainWindowGroup, "activetab", value);
            RaiseChangedQualified($"{MainWindowGroup}/activetab");
        }
    }

    /// <summary>Bookpath of the most recently inserted file (Insert &#8594; File menu).</summary>
    public string LastInsertedFile
    {
        get => ReadStringFrom(MainWindowGroup, "lastinsertedfile", string.Empty);
        set
        {
            _file.SetRaw(MainWindowGroup, "lastinsertedfile", value);
            RaiseChangedQualified($"{MainWindowGroup}/lastinsertedfile");
        }
    }

    /// <summary>
    /// Clipboard history (Edit &#8594; Paste From Clipboard History menu), newest entry first.
    /// Storage only; tracking of system clipboard changes lives in the App layer.
    /// </summary>
    public IReadOnlyList<string> ClipboardHistory
    {
        get => (_file.GetRaw(Group, "clipboardhistory") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(Group, "clipboardhistory", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/clipboardhistory");
        }
    }

    /// <summary>
    /// Recently inserted special characters (Insert &#8594; Special Character menu), newest
    /// first.
    /// </summary>
    public IReadOnlyList<string> RecentSpecialCharacters
    {
        get => (_file.GetRaw(Group, "recentspecialchars") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(Group, "recentspecialchars", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/recentspecialchars");
        }
    }

    /// <summary>
    /// The steps checked in the Cleanup dialog the last time it was confirmed (remembered between sessions);
    /// unknown names are skipped. Empty by default.
    /// </summary>
    public IReadOnlyList<CleanupStep> CleanupEnabledSteps
    {
        get => (_file.GetRaw(Group, "cleanupsteps") as JsonArray)?
            .Select(n => Enum.TryParse(n?.GetValue<string>(), out CleanupStep step) ? (CleanupStep?)step : null)
            .Where(step => step is not null)
            .Select(step => step!.Value)
            .Distinct()
            .ToList() ?? new List<CleanupStep>();

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _file.SetRaw(Group, "cleanupsteps", new JsonArray(value.Select(step => JsonValue.Create(step.ToString())).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/cleanupsteps");
        }
    }

    /// <summary>
    /// The steps checked in the "Standardize EPUB" dialog the last time it was applied (remembered between sessions);
    /// unknown names are skipped. All steps until the dialog is applied for the first time.
    /// </summary>
    public IReadOnlyList<StandardizationStep> StandardizationEnabledSteps
    {
        get => _file.GetRaw(Group, "standardizationsteps") is JsonArray array
            ? array
                .Select(n => Enum.TryParse(n?.GetValue<string>(), out StandardizationStep step) ? (StandardizationStep?)step : null)
                .Where(step => step is not null)
                .Select(step => step!.Value)
                .Distinct()
                .ToList()
            : EpubStandardization.AllSteps;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _file.SetRaw(Group, "standardizationsteps", new JsonArray(value.Select(step => JsonValue.Create(step.ToString())).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/standardizationsteps");
        }
    }

    /// <summary>
    /// Favorite special characters (Insert &#8594; Special Character menu) — unlike
    /// <see cref="RecentSpecialCharacters"/>, the list is managed manually by the user only
    /// (no limit, no automatic LRU) and keeps the order in which items were added.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpecialCharacters
    {
        get => (_file.GetRaw(Group, "favoritespecialchars") as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        set
        {
            _file.SetRaw(Group, "favoritespecialchars", new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
            RaiseChangedQualified($"{Group}/favoritespecialchars");
        }
    }

    // ------------------------------------------- generic map sections -- //

    /// <summary>
    /// Reads an arbitrary settings group as a string→string map. Used by the GUI layer to
    /// persist registries with a variable number of keys (keyboard shortcuts, toolbar
    /// layout).
    /// </summary>
    /// <param name="group">Group name (e.g. <c>keyboard_shortcuts</c>).</param>
    public IReadOnlyDictionary<string, string> GetStringMap(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        return _file.GetStringGroup(group);
    }

    /// <summary>
    /// Replaces the whole <paramref name="group"/> group with the given set of pairs. An empty set
    /// removes the group. The change is in memory — <see cref="Save"/> is needed to persist it.
    /// </summary>
    public void SetStringMap(string group, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(values);
        _file.SetStringGroup(group, values);
        RaiseChangedQualified(group);
    }

    // -------------------------------------------------- Find &amp; Replace -- //

    /// <summary>
    /// Reads the persisted state of the Find &amp; Replace panel (the <c>find_replace</c> group).
    /// Missing keys take their default values.
    /// </summary>
    public FindReplaceSettings GetFindReplaceSettings()
    {
        return new FindReplaceSettings
        {
            FindHistory = ReadStringListFrom(FindReplaceGroup, "find_strings"),
            ReplaceHistory = ReadStringListFrom(FindReplaceGroup, "replace_strings"),
            Mode = (SearchMode)ReadIntFrom(FindReplaceGroup, "search_mode", (int)SearchMode.Normal),
            Direction = (SearchDirection)ReadIntFrom(FindReplaceGroup, "search_direction", (int)SearchDirection.Down),
            LookWhere = (LookWhere)ReadIntFrom(FindReplaceGroup, "look_where", (int)LookWhere.CurrentFile),
            OptionWrap = ReadBoolFrom(FindReplaceGroup, "optionwrap", true),
            RegexDotAll = ReadBoolFrom(FindReplaceGroup, "regexoptiondotall", false),
            RegexMinimalMatch = ReadBoolFrom(FindReplaceGroup, "regexoptionminimalmatch", false),
            RegexUnicodeProperty = ReadBoolFrom(FindReplaceGroup, "regexoptionunicodeproperty", false),
            RegexTextOnly = ReadBoolFrom(FindReplaceGroup, "regexoptiontextonly", false),
            RegexAutoTokenise = ReadBoolFrom(FindReplaceGroup, "regexoptionautotokenise", false),
            HighlightAllMatches = ReadBoolFrom(FindReplaceGroup, "highlight_all_matches", false),
            PanelVisible = ReadBoolFrom(FindReplaceGroup, "visible", false),
        };
    }

    /// <summary>
    /// Saves the state of the Find &amp; Replace panel. History is truncated to
    /// <see cref="FindReplaceSettings.MaxHistory"/> entries. The change is in memory —
    /// <see cref="Save"/> is needed to persist it.
    /// </summary>
    public void SaveFindReplaceSettings(FindReplaceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        WriteStringListTo(FindReplaceGroup, "find_strings", Trim(settings.FindHistory));
        WriteStringListTo(FindReplaceGroup, "replace_strings", Trim(settings.ReplaceHistory));
        _file.SetRaw(FindReplaceGroup, "search_mode", (int)settings.Mode);
        _file.SetRaw(FindReplaceGroup, "search_direction", (int)settings.Direction);
        _file.SetRaw(FindReplaceGroup, "look_where", (int)settings.LookWhere);
        _file.SetRaw(FindReplaceGroup, "optionwrap", settings.OptionWrap);
        _file.SetRaw(FindReplaceGroup, "regexoptiondotall", settings.RegexDotAll);
        _file.SetRaw(FindReplaceGroup, "regexoptionminimalmatch", settings.RegexMinimalMatch);
        _file.SetRaw(FindReplaceGroup, "regexoptionunicodeproperty", settings.RegexUnicodeProperty);
        _file.SetRaw(FindReplaceGroup, "regexoptiontextonly", settings.RegexTextOnly);
        _file.SetRaw(FindReplaceGroup, "regexoptionautotokenise", settings.RegexAutoTokenise);
        _file.SetRaw(FindReplaceGroup, "highlight_all_matches", settings.HighlightAllMatches);
        _file.SetRaw(FindReplaceGroup, "visible", settings.PanelVisible);
        RaiseChangedQualified(FindReplaceGroup);

        static IEnumerable<string> Trim(IReadOnlyList<string> history) =>
            history.Take(FindReplaceSettings.MaxHistory);
    }

    // -------------------------------------------------------- internals -- //

    private string ReadString(string key, string fallback) => ReadStringFrom(Group, key, fallback);

    private string ReadStringFrom(string group, string key, string fallback)
    {
        return _file.GetRaw(group, key) is JsonValue v && v.TryGetValue(out string? s) ? s : fallback;
    }

    private bool ReadBool(string key, bool fallback)
    {
        return _file.GetRaw(Group, key) is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
    }

    private bool ReadBoolFrom(string group, string key, bool fallback)
    {
        return _file.GetRaw(group, key) is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
    }

    private int ReadInt(string key, int fallback) => ReadIntFrom(Group, key, fallback);

    private int ReadIntFrom(string group, string key, int fallback)
    {
        if (_file.GetRaw(group, key) is not JsonValue v)
        {
            return fallback;
        }

        // A node written in the current session is a primitive (int); after reloading from the
        // file it is a JSON element (readable as double) — both are handled.
        if (v.TryGetValue(out int i))
        {
            return i;
        }

        return v.TryGetValue(out double d) ? (int)d : fallback;
    }

    private List<string> ReadStringListFrom(string group, string key)
    {
        return _file.GetRaw(group, key) is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList()
            : new List<string>();
    }

    private void WriteStringListTo(string group, string key, IEnumerable<string> value)
    {
        _file.SetRaw(group, key, new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
    }

    private float ReadFloat(string key, float fallback)
    {
        if (_file.GetRaw(Group, key) is not JsonValue v)
        {
            return fallback;
        }

        // As in ReadIntFrom: a node written in the current session is a primitive (float); after
        // reloading from the file it is a JSON element (readable as double) — both are handled.
        if (v.TryGetValue(out float f))
        {
            return f;
        }

        return v.TryGetValue(out double d) ? (float)d : fallback;
    }

    private double ReadDouble(string key, double fallback)
    {
        return _file.GetRaw(Group, key) is JsonValue v && v.TryGetValue(out double d) ? d : fallback;
    }

    private List<string>? ReadStringList(string key)
    {
        return _file.GetRaw(Group, key) is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>() ?? string.Empty).ToList()
            : null;
    }

    private void Write(string key, string value)
    {
        _file.SetRaw(Group, key, value);
        RaiseChanged(key);
    }

    private void Write(string key, bool value)
    {
        _file.SetRaw(Group, key, value);
        RaiseChanged(key);
    }

    private void Write(string key, int value)
    {
        _file.SetRaw(Group, key, value);
        RaiseChanged(key);
    }

    private void Write(string key, float value)
    {
        _file.SetRaw(Group, key, value);
        RaiseChanged(key);
    }

    private void Write(string key, double value)
    {
        _file.SetRaw(Group, key, value);
        RaiseChanged(key);
    }

    private void Write(string key, IEnumerable<string> value)
    {
        _file.SetRaw(Group, key, new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray<JsonNode?>()));
        RaiseChanged(key);
    }

    private void WriteSilent(string key, string value) => _file.SetRaw(Group, key, value);

    private void WriteSilent(string key, int value) => _file.SetRaw(Group, key, value);

    private IReadOnlyList<(ushort Code, string Name)> ReadEntityPairs()
    {
        if (_file.GetRaw(Group, "preserve_entities") is JsonArray arr)
        {
            List<(ushort, string)> pairs = new();
            foreach (JsonNode? node in arr)
            {
                if (node is JsonObject obj
                    && obj["code"] is JsonValue cv && TryGetCode(cv, out ushort code)
                    && obj["name"] is JsonValue nv && nv.TryGetValue(out string? name))
                {
                    pairs.Add((code, name));
                }
            }

            return pairs;
        }

        return new[] { ((ushort)160, "&#160;") };
    }

    // As in ReadIntFrom: a node written in the current session is a primitive (ushort); after
    // reloading from the file it is a JSON element (readable as double) — both are handled.
    private static bool TryGetCode(JsonValue value, out ushort code)
    {
        if (value.TryGetValue(out ushort direct))
        {
            code = direct;
            return true;
        }

        if (value.TryGetValue(out double d))
        {
            code = (ushort)d;
            return true;
        }

        code = 0;
        return false;
    }

    private void WriteEntityPairs(IReadOnlyList<(ushort Code, string Name)> pairs)
    {
        JsonArray arr = new();
        foreach ((ushort code, string name) in pairs)
        {
            arr.Add(new JsonObject { ["code"] = code, ["name"] = name });
        }

        _file.SetRaw(Group, "preserve_entities", arr);
        RaiseChanged("preserve_entities");
    }

    private CodeViewAppearance ReadCodeView(string prefix, CodeViewAppearance defaults)
    {
        return new CodeViewAppearance
        {
            FontFamily = ReadString(prefix + "font_family_standard", defaults.FontFamily),
            FontSize = ReadInt(prefix + "font_size", defaults.FontSize),
            CssCommentColor = ReadString(prefix + "css_comment_color", defaults.CssCommentColor),
            CssPropertyColor = ReadString(prefix + "css_property_color", defaults.CssPropertyColor),
            CssQuoteColor = ReadString(prefix + "css_quote_color", defaults.CssQuoteColor),
            CssSelectorColor = ReadString(prefix + "css_selector_color", defaults.CssSelectorColor),
            CssValueColor = ReadString(prefix + "css_value_color", defaults.CssValueColor),
            LineHighlightColor = ReadString(prefix + "line_highlight_color", defaults.LineHighlightColor),
            LineNumberBackgroundColor = ReadString(prefix + "line_number_background_color", defaults.LineNumberBackgroundColor),
            LineNumberForegroundColor = ReadString(prefix + "line_number_foreground_color", defaults.LineNumberForegroundColor),
            SpellingUnderlineColor = ReadString(prefix + "spelling_underline_color", defaults.SpellingUnderlineColor),
            XhtmlAttributeNameColor = ReadString(prefix + "xhtml_attribute_name_color", defaults.XhtmlAttributeNameColor),
            XhtmlAttributeValueColor = ReadString(prefix + "xhtml_attribute_value_color", defaults.XhtmlAttributeValueColor),
            XhtmlCssColor = ReadString(prefix + "xhtml_css_color", defaults.XhtmlCssColor),
            XhtmlCssCommentColor = ReadString(prefix + "xhtml_css_comment_color", defaults.XhtmlCssCommentColor),
            XhtmlDoctypeColor = ReadString(prefix + "xhtml_doctype_color", defaults.XhtmlDoctypeColor),
            XhtmlEntityColor = ReadString(prefix + "xhtml_entity_color", defaults.XhtmlEntityColor),
            XhtmlHtmlColor = ReadString(prefix + "xhtml_html_color", defaults.XhtmlHtmlColor),
            XhtmlHtmlCommentColor = ReadString(prefix + "xhtml_html_comment_color", defaults.XhtmlHtmlCommentColor),
            CssSpecialSelectorColor = ReadString(prefix + "css_special_selector_color", defaults.CssSpecialSelectorColor),
            CssAtRuleColor = ReadString(prefix + "css_at_rule_color", defaults.CssAtRuleColor),
            CssConstantColor = ReadString(prefix + "css_constant_color", defaults.CssConstantColor),
            XhtmlNamespacePrefixColor = ReadString(prefix + "xhtml_namespace_prefix_color", defaults.XhtmlNamespacePrefixColor),
            LinkColor = ReadString(prefix + "link_color", defaults.LinkColor),
            ErrorUnderlineColor = ReadString(prefix + "error_underline_color", defaults.ErrorUnderlineColor),
            SearchMatchBackgroundColor = ReadString(prefix + "search_match_background_color", defaults.SearchMatchBackgroundColor),
            SelectionBackgroundColor = ReadString(prefix + "selection_background_color", defaults.SelectionBackgroundColor),
        };
    }

    private void WriteCodeView(string prefix, CodeViewAppearance a)
    {
        WriteSilent(prefix + "font_family_standard", a.FontFamily);
        WriteSilent(prefix + "font_size", a.FontSize);
        WriteSilent(prefix + "css_comment_color", a.CssCommentColor);
        WriteSilent(prefix + "css_property_color", a.CssPropertyColor);
        WriteSilent(prefix + "css_quote_color", a.CssQuoteColor);
        WriteSilent(prefix + "css_selector_color", a.CssSelectorColor);
        WriteSilent(prefix + "css_value_color", a.CssValueColor);
        WriteSilent(prefix + "line_highlight_color", a.LineHighlightColor);
        WriteSilent(prefix + "line_number_background_color", a.LineNumberBackgroundColor);
        WriteSilent(prefix + "line_number_foreground_color", a.LineNumberForegroundColor);
        WriteSilent(prefix + "spelling_underline_color", a.SpellingUnderlineColor);
        WriteSilent(prefix + "xhtml_attribute_name_color", a.XhtmlAttributeNameColor);
        WriteSilent(prefix + "xhtml_attribute_value_color", a.XhtmlAttributeValueColor);
        WriteSilent(prefix + "xhtml_css_color", a.XhtmlCssColor);
        WriteSilent(prefix + "xhtml_css_comment_color", a.XhtmlCssCommentColor);
        WriteSilent(prefix + "xhtml_doctype_color", a.XhtmlDoctypeColor);
        WriteSilent(prefix + "xhtml_entity_color", a.XhtmlEntityColor);
        WriteSilent(prefix + "xhtml_html_color", a.XhtmlHtmlColor);
        WriteSilent(prefix + "xhtml_html_comment_color", a.XhtmlHtmlCommentColor);
        WriteSilent(prefix + "css_special_selector_color", a.CssSpecialSelectorColor);
        WriteSilent(prefix + "css_at_rule_color", a.CssAtRuleColor);
        WriteSilent(prefix + "css_constant_color", a.CssConstantColor);
        WriteSilent(prefix + "xhtml_namespace_prefix_color", a.XhtmlNamespacePrefixColor);
        WriteSilent(prefix + "link_color", a.LinkColor);
        WriteSilent(prefix + "error_underline_color", a.ErrorUnderlineColor);
        WriteSilent(prefix + "search_match_background_color", a.SearchMatchBackgroundColor);
        WriteSilent(prefix + "selection_background_color", a.SelectionBackgroundColor);
    }

    private static IEnumerable<string> AppearanceKeys()
    {
        yield return "preview_font_family_standard";
        yield return "preview_font_family_serif";
        yield return "preview_font_family_sans_serif";
        yield return "preview_font_size";
        yield return "special_character_font_family";
        yield return "special_character_font_size";
        yield return "warning_font_size";
        yield return "warning_color_light";
        yield return "warning_color_dark";
        yield return "open_tag_hint_font_family";
        yield return "open_tag_hint_font_size";
        yield return "open_tag_hint_light_background";
        yield return "open_tag_hint_light_foreground";
        yield return "open_tag_hint_dark_background";
        yield return "open_tag_hint_dark_foreground";

        string[] suffixes =
        {
            "font_family_standard", "font_size", "css_comment_color", "css_property_color",
            "css_quote_color", "css_selector_color", "css_value_color", "line_highlight_color",
            "line_number_background_color", "line_number_foreground_color", "spelling_underline_color",
            "xhtml_attribute_name_color", "xhtml_attribute_value_color", "xhtml_css_color",
            "xhtml_css_comment_color", "xhtml_doctype_color", "xhtml_entity_color",
            "xhtml_html_color", "xhtml_html_comment_color",
            "css_special_selector_color", "css_at_rule_color", "css_constant_color",
            "xhtml_namespace_prefix_color", "link_color", "error_underline_color",
            "search_match_background_color", "selection_background_color",
        };

        foreach (string suffix in suffixes)
        {
            yield return "code_view_" + suffix;
            yield return "cv_dark_" + suffix;
        }
    }

    private void RaiseChanged(string key) => RaiseChangedQualified($"{Group}/{key}");

    private void RaiseChangedQualified(string qualifiedKey)
    {
        if (IsDraft && !_draftRaised.Contains(qualifiedKey))
        {
            _draftRaised.Add(qualifiedKey);
        }

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(qualifiedKey));
    }
}
