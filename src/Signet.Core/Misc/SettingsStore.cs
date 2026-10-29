using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.MainUI;
using Signet.Core.Search;

namespace Signet.Core.Misc;

/// <summary>
/// Typed access layer for configurable user settings, stored as JSON (most keys live in the
/// <c>user_preferences</c> group).
/// </summary>
/// <remarks>
/// Colors in the appearance structures are stored as <c>#RRGGBB</c> strings (Core does not
/// depend on Avalonia). The store also holds window geometry and the recent files list. The class is not thread-safe.
/// </remarks>
public sealed class SettingsStore
{
    private const string Group = "user_preferences";
    private const string DockLayoutKey = "main_window_dock_layout";
    private const string GeometryGroup = "geometry";
    private const string MainWindowGroup = "main_window";
    private const string FindReplaceGroup = "find_replace";

    private const float ZoomNormal = 1.0f;
    private const int RecentFilesDefault = 9;
    private const int StartScreenRecentFilesDefault = 30;

    /// <summary>Upper bound of <see cref="ClipboardHistoryLimit"/> (0 = disabled).</summary>
    public const int ClipboardHistoryMax = 20;

    /// <summary>Upper bound of <see cref="RecentFilesLimit"/> (the File menu shows the first entries of <see cref="RecentFiles"/>).</summary>
    public const int RecentFilesMenuMax = 20;

    /// <summary>
    /// Upper bound of <see cref="StartScreenRecentFilesLimit"/> — and also how many paths
    /// <see cref="RecentFiles"/> keeps, so that lowering a limit does not lose history.
    /// </summary>
    public const int RecentFilesMax = 100;

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

        // Other instances of the program share the file: their recent files are merged with ours when it is saved.
        _file.AddRecentList(MainWindowGroup, "recentfiles", RecentFilesMax);
        RemoveObsoleteKeys();
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
    public void Reload()
    {
        _file.Reload();
        RemoveObsoleteKeys();
    }

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

    /// <summary>
    /// Interface font weight (<see cref="FontWeightScale"/>); 0 = not set (the weight each control has by itself, Regular for
    /// the text).
    /// </summary>
    public int UiFontWeight
    {
        get => FontWeightScale.Normalize(ReadInt("ui_font_weight", 0));
        set => Write("ui_font_weight", FontWeightScale.Normalize(value));
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
    /// Delay (ms) of the live preview refresh after a change in Code Editor (default 1000 ms; a value
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

    /// <summary>Whether Preview clicks on footnote and endnote references move Code View to the note.</summary>
    public bool PreviewFollowFootnotesInCodeView
    {
        get => ReadBool("preview_follow_footnotes_in_code_view", true);
        set => Write("preview_follow_footnotes_in_code_view", value);
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

    /// <summary>
    /// Whether the font size is shown for a moment in the middle of a code editor while it is changed with Ctrl + mouse
    /// wheel (on by default).
    /// </summary>
    public bool ShowZoomHint
    {
        get => ReadBool("show_zoom_hint", true);
        set => Write("show_zoom_hint", value);
    }

    /// <summary>
    /// Whether the title bar of the main window shows the full path of the open book instead of just its file name (off by
    /// default).
    /// </summary>
    public bool FullPathInTitle
    {
        get => ReadBool("window_title_full_path", false);
        set => Write("window_title_full_path", value);
    }

    /// <summary>
    /// Whether the Preferences window is modal (on by default): the main window waits until it is closed. Off, it opens
    /// as a normal window and the main window can be used meanwhile. Read when the window opens.
    /// </summary>
    public bool PreferencesWindowModal
    {
        get => ReadBool("preferences_window_modal", true);
        set => Write("preferences_window_modal", value);
    }

    /// <summary>Whether to use a double-width text cursor.</summary>
    public bool UiDoubleWidthTextCursor
    {
        get => ReadBool("ui_doublewidth_textcursor", false);
        set => Write("ui_doublewidth_textcursor", value);
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

    /// <summary>The last values of the "Mass Rename" window of the Book Browser. Always read normalized.</summary>
    public MassRenameOptions MassRename
    {
        get
        {
            MassRenameOptions d = MassRenameOptions.Default;
            string padding = ReadString("mass_rename_padding_char", d.PaddingChar.ToString());
            return new MassRenameOptions(
                ReadString("mass_rename_prefix", d.Prefix),
                ReadInt("mass_rename_start_number", d.StartNumber),
                padding.Length > 0 ? padding[0] : d.PaddingChar,
                ReadInt("mass_rename_min_length", d.MinLength),
                ReadBool("mass_rename_book_order", d.BookOrder)).Normalized();
        }

        set
        {
            MassRenameOptions v = value.Normalized();
            Write("mass_rename_prefix", v.Prefix);
            Write("mass_rename_start_number", v.StartNumber);
            Write("mass_rename_padding_char", v.PaddingChar == '\0' ? string.Empty : v.PaddingChar.ToString());
            Write("mass_rename_min_length", v.MinLength);
            Write("mass_rename_book_order", v.BookOrder);
        }
    }

    /// <summary>
    /// The dock layout of the main window — the assignment of panels to regions, their order and the proportions, not
    /// just their visibility (which is kept separately via <see cref="GetStringMap"/> under the docks group).
    /// <see langword="null"/> when the layout has not been saved yet (first run) or cannot be read.
    /// </summary>
    public DockLayoutState? MainWindowDockLayout
    {
        get => _file.GetRaw(Group, DockLayoutKey) is JsonObject obj ? ReadDockLayout(obj) : null;
        set
        {
            _file.SetRaw(Group, DockLayoutKey, value is null ? null : WriteDockLayout(value));
            RaiseChanged(DockLayoutKey);
        }
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

    /// <summary>Text view (Code Editor) zoom factor.</summary>
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
    /// Whether Code Editor wraps long lines to the window width (View → Word Wrap). Defaults to
    /// <c>true</c>.
    /// </summary>
    public bool CodeEditorWordWrap
    {
        get => ReadBool("code_editor_word_wrap", true);
        set => Write("code_editor_word_wrap", value);
    }

    /// <summary>
    /// Whether the Live CSS panel lists, above the rules of the inspected element, the properties it inherits from
    /// its ancestors ("Show inherited"). Off by default.
    /// </summary>
    public bool LiveCssShowInherited
    {
        get => ReadBool("live_css_show_inherited", false);
        set => Write("live_css_show_inherited", value);
    }

    /// <summary>
    /// Whether Code Editor uses extended highlighting (CSS inside <c>&lt;style&gt;</c>, richer CSS,
    /// errors, links, namespace prefixes, bold/italic). Enabled by default; <c>false</c> uses
    /// the basic highlighting.
    /// </summary>
    public bool CodeEditorExtendedHighlighting
    {
        get => ReadBool("code_editor_extended_highlighting", true);
        set => Write("code_editor_extended_highlighting", value);
    }

    /// <summary>
    /// Whether the Book Browser selects the file of the tab that became the active one (also by focusing a tab in a window
    /// of its own). Enabled by default.
    /// </summary>
    public bool BookBrowserFollowsActiveTab
    {
        get => ReadBool("book_browser_follows_active_tab", true);
        set => Write("book_browser_follows_active_tab", value);
    }

    /// <summary>
    /// Whether the document tabs that do not fit in a row are laid out in several rows instead of being scrolled with
    /// arrows. Off by default.
    /// </summary>
    public bool CodeEditorMultilineTabs
    {
        get => ReadBool("code_editor_multiline_tabs", false);
        set => Write("code_editor_multiline_tabs", value);
    }

    /// <summary>
    /// How the Find &amp; Replace panel's action buttons show their icon and text. Icon and text by default.
    /// </summary>
    public FindActionButtonDisplay FindActionButtonDisplay
    {
        get => Enum.TryParse(
            ReadString("find_action_button_display", nameof(FindActionButtonDisplay.IconAndText)),
            ignoreCase: true, out FindActionButtonDisplay parsed)
            ? parsed
            : FindActionButtonDisplay.IconAndText;
        set => Write("find_action_button_display", value.ToString());
    }

    /// <summary>
    /// How the Saved Searches window's batch action buttons (Load Search, Find Next, Replace, …) show their icon
    /// and text. Icon and text by default.
    /// </summary>
    public FindActionButtonDisplay SearchEditorActionButtonDisplay
    {
        get => Enum.TryParse(
            ReadString("search_editor_action_button_display", nameof(FindActionButtonDisplay.IconAndText)),
            ignoreCase: true, out FindActionButtonDisplay parsed)
            ? parsed
            : FindActionButtonDisplay.IconAndText;
        set => Write("search_editor_action_button_display", value.ToString());
    }

    /// <summary>
    /// Whether Code Editor closes the open tag after <c>&lt;/</c> is typed (enabled by default).
    /// </summary>
    public bool CodeEditorAutoCloseTags
    {
        get => ReadBool("code_editor_auto_close_tags", true);
        set => Write("code_editor_auto_close_tags", value);
    }

    /// <summary>
    /// Whether text pasted into Code Editor is normalized to Unicode NFC (a letter followed by a combining accent becomes
    /// one precomposed character; enabled by default).
    /// </summary>
    public bool CodeEditorPasteNormalizeNfc
    {
        get => ReadBool("code_editor_paste_normalize_nfc", true);
        set => Write("code_editor_paste_normalize_nfc", value);
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
    /// Whether the "Find Usages" panel wraps the text of a cell that does not fit in the column onto more lines (the
    /// row grows) instead of cutting it with an ellipsis; remembered between sessions, off by default.
    /// </summary>
    public bool FindUsagesWrapRows
    {
        get => ReadBool("find_usages_wrap_rows", false);
        set => Write("find_usages_wrap_rows", value);
    }

    /// <summary>
    /// Whether the first column ("Usage": the files) of the "Find Usages" panel is as wide as its widest entry (plus a
    /// small margin) every time the panel shows a search. When off, the panel keeps the width the column had last
    /// (<see cref="FindUsagesUsageColumnWidth"/>). On by default.
    /// </summary>
    public bool FindUsagesFitUsageColumn
    {
        get => ReadBool("find_usages_fit_usage_column", true);
        set => Write("find_usages_fit_usage_column", value);
    }

    /// <summary>
    /// The width (px) the user gave the "Usage" column of the "Find Usages" panel, used while
    /// <see cref="FindUsagesFitUsageColumn"/> is off; 0 = none yet. Window state, not a preference — written outside the
    /// Save / Cancel draft of the Preferences window.
    /// </summary>
    public int FindUsagesUsageColumnWidth
    {
        get => Math.Max(0, ReadIntFrom(MainWindowGroup, "find_usages_usage_column_width", 0));
        set => _file.SetRaw(MainWindowGroup, "find_usages_usage_column_width", Math.Max(0, value));
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

    /// <summary>Upper bound of <see cref="CodeEditorOpenTagHintDelayMs"/>.</summary>
    public const int OpenTagHintDelayMaxMs = 5000;

    /// <summary>
    /// Whether Code Editor shows the matching opening tag (as in the source, with its line number) in a tooltip when the
    /// mouse rests on a closing tag. On by default.
    /// </summary>
    public bool CodeEditorOpenTagHint
    {
        get => ReadBool("code_editor_open_tag_hint", true);
        set => Write("code_editor_open_tag_hint", value);
    }

    /// <summary>
    /// Look of the opening tag tooltip (<see cref="CodeEditorOpenTagHint"/>): font and colors. Always
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
                ReadInt("open_tag_hint_font_weight", d.FontWeight)).Normalized();
        }

        set
        {
            OpenTagHintAppearance v = value.Normalized();
            WriteSilent("open_tag_hint_font_family", v.FontFamily);
            WriteSilent("open_tag_hint_font_size", v.FontSize);
            WriteSilent("open_tag_hint_font_weight", v.FontWeight);
            RaiseChanged("open_tag_hint_appearance");
        }
    }

    /// <summary>
    /// The font of the line numbers of Code Editor (empty / 0 = the editor font). Always read normalized
    /// (<see cref="LineNumberFontAppearance.Normalized"/>).
    /// </summary>
    public LineNumberFontAppearance LineNumberFontAppearance
    {
        get
        {
            LineNumberFontAppearance d = LineNumberFontAppearance.Default;
            return new LineNumberFontAppearance(
                ReadString("code_editor_line_numbers_font_family", d.FontFamily),
                ReadInt("code_editor_line_numbers_font_size", d.FontSize),
                ReadInt("code_editor_line_numbers_font_weight", d.FontWeight)).Normalized();
        }

        set
        {
            LineNumberFontAppearance v = value.Normalized();
            WriteSilent("code_editor_line_numbers_font_family", v.FontFamily);
            WriteSilent("code_editor_line_numbers_font_size", v.FontSize);
            WriteSilent("code_editor_line_numbers_font_weight", v.FontWeight);
            RaiseChanged("code_editor_line_numbers_font");
        }
    }

    /// <summary>
    /// The font of the titles of the document tabs (empty / 0 = the interface font at the default size). Always read
    /// normalized (<see cref="DocumentTabFontAppearance.Normalized"/>).
    /// </summary>
    public DocumentTabFontAppearance DocumentTabFontAppearance
    {
        get
        {
            DocumentTabFontAppearance d = DocumentTabFontAppearance.Default;
            return new DocumentTabFontAppearance(
                ReadString("document_tab_font_family", d.FontFamily),
                ReadInt("document_tab_font_size", d.FontSize),
                ReadInt("document_tab_font_weight", d.FontWeight)).Normalized();
        }

        set
        {
            DocumentTabFontAppearance v = value.Normalized();
            WriteSilent("document_tab_font_family", v.FontFamily);
            WriteSilent("document_tab_font_size", v.FontSize);
            WriteSilent("document_tab_font_weight", v.FontWeight);
            RaiseChanged("document_tab_font");
        }
    }

    /// <summary>
    /// Whether the document tabs use the font of a tool panel's title bar (<see cref="ToolHeaderFontAppearance"/>) instead
    /// of their own (<see cref="DocumentTabFontAppearance"/>) — off by default. The own font stays stored while this is
    /// on, for when it is turned off again.
    /// </summary>
    public bool DocumentTabFontInheritsToolHeader
    {
        get => ReadBool("document_tab_font_inherit_tool_header", false);
        set
        {
            WriteSilent("document_tab_font_inherit_tool_header", value);
            RaiseChanged("document_tab_font");
        }
    }

    /// <summary>The font the document tabs use now: their own, or — while <see cref="DocumentTabFontInheritsToolHeader"/> — a
    /// tool panel's title bar font.</summary>
    public DocumentTabFontAppearance EffectiveDocumentTabFontAppearance
    {
        get
        {
            if (!DocumentTabFontInheritsToolHeader)
            {
                return DocumentTabFontAppearance;
            }

            ToolHeaderFontAppearance parent = ToolHeaderFontAppearance;
            return new DocumentTabFontAppearance(parent.FontFamily, parent.FontSize, parent.FontWeight);
        }
    }

    /// <summary>
    /// The font of the title bar of a tool panel (empty / 0 = the interface font at the default size). Always read
    /// normalized (<see cref="ToolHeaderFontAppearance.Normalized"/>).
    /// </summary>
    public ToolHeaderFontAppearance ToolHeaderFontAppearance
    {
        get
        {
            ToolHeaderFontAppearance d = ToolHeaderFontAppearance.Default;
            return new ToolHeaderFontAppearance(
                ReadString("tool_header_font_family", d.FontFamily),
                ReadInt("tool_header_font_size", d.FontSize),
                ReadInt("tool_header_font_weight", d.FontWeight)).Normalized();
        }

        set
        {
            ToolHeaderFontAppearance v = value.Normalized();
            WriteSilent("tool_header_font_family", v.FontFamily);
            WriteSilent("tool_header_font_size", v.FontSize);
            WriteSilent("tool_header_font_weight", v.FontWeight);
            RaiseChanged("tool_header_font");
        }
    }

    /// <summary>
    /// The font of the rows of the Notifications panel and the space between them (empty / 0 = the interface font at
    /// the default size and spacing). Always read normalized (<see cref="NotificationsAppearance.Normalized"/>).
    /// </summary>
    public NotificationsAppearance NotificationsAppearance
    {
        get
        {
            NotificationsAppearance d = NotificationsAppearance.Default;
            return new NotificationsAppearance(
                ReadString("notifications_timestamp_font_family", d.TimestampFontFamily),
                ReadInt("notifications_timestamp_font_size", d.TimestampFontSize),
                ReadInt("notifications_timestamp_font_weight", d.TimestampFontWeight),
                ReadString("notifications_message_font_family", d.MessageFontFamily),
                ReadInt("notifications_message_font_size", d.MessageFontSize),
                ReadInt("notifications_message_font_weight", d.MessageFontWeight),
                ReadInt("notifications_entry_spacing", d.EntrySpacing)).Normalized();
        }

        set
        {
            NotificationsAppearance v = value.Normalized();
            WriteSilent("notifications_timestamp_font_family", v.TimestampFontFamily);
            WriteSilent("notifications_timestamp_font_size", v.TimestampFontSize);
            WriteSilent("notifications_timestamp_font_weight", v.TimestampFontWeight);
            WriteSilent("notifications_message_font_family", v.MessageFontFamily);
            WriteSilent("notifications_message_font_size", v.MessageFontSize);
            WriteSilent("notifications_message_font_weight", v.MessageFontWeight);
            WriteSilent("notifications_entry_spacing", v.EntrySpacing);
            RaiseChanged("notifications_appearance");
        }
    }

    /// <summary>
    /// Whether the Notifications panel's timestamp column uses the font of a tool panel's title bar
    /// (<see cref="ToolHeaderFontAppearance"/>) instead of its own (<see cref="NotificationsAppearance"/>) — off by
    /// default. The own font stays stored while this is on, for when it is turned off again.
    /// </summary>
    public bool NotificationsTimestampFontInheritsToolHeader
    {
        get => ReadBool("notifications_timestamp_font_inherit_tool_header", false);
        set
        {
            WriteSilent("notifications_timestamp_font_inherit_tool_header", value);
            RaiseChanged("notifications_appearance");
        }
    }

    /// <summary>
    /// Whether the Notifications panel's message column uses the font of a tool panel's title bar
    /// (<see cref="ToolHeaderFontAppearance"/>) instead of its own (<see cref="NotificationsAppearance"/>) — off by
    /// default. The own font stays stored while this is on, for when it is turned off again.
    /// </summary>
    public bool NotificationsMessageFontInheritsToolHeader
    {
        get => ReadBool("notifications_message_font_inherit_tool_header", false);
        set
        {
            WriteSilent("notifications_message_font_inherit_tool_header", value);
            RaiseChanged("notifications_appearance");
        }
    }

    /// <summary>The look the Notifications panel uses now: its own fonts, or — per column, while
    /// <see cref="NotificationsTimestampFontInheritsToolHeader"/> / <see cref="NotificationsMessageFontInheritsToolHeader"/> —
    /// a tool panel's title bar font; the spacing is always its own.</summary>
    public NotificationsAppearance EffectiveNotificationsAppearance
    {
        get
        {
            NotificationsAppearance own = NotificationsAppearance;
            if (!NotificationsTimestampFontInheritsToolHeader && !NotificationsMessageFontInheritsToolHeader)
            {
                return own;
            }

            ToolHeaderFontAppearance parent = ToolHeaderFontAppearance;
            return new NotificationsAppearance(
                NotificationsTimestampFontInheritsToolHeader ? parent.FontFamily : own.TimestampFontFamily,
                NotificationsTimestampFontInheritsToolHeader ? parent.FontSize : own.TimestampFontSize,
                NotificationsTimestampFontInheritsToolHeader ? parent.FontWeight : own.TimestampFontWeight,
                NotificationsMessageFontInheritsToolHeader ? parent.FontFamily : own.MessageFontFamily,
                NotificationsMessageFontInheritsToolHeader ? parent.FontSize : own.MessageFontSize,
                NotificationsMessageFontInheritsToolHeader ? parent.FontWeight : own.MessageFontWeight,
                own.EntrySpacing);
        }
    }

    /// <summary>Whether a message that does not fit the message column wraps onto more lines (the row grows) instead
    /// of being cut with an ellipsis.</summary>
    public bool NotificationsWrapRows
    {
        get => ReadBool("notifications_wrap_rows", false);
        set => Write("notifications_wrap_rows", value);
    }

    /// <summary>
    /// After how many milliseconds of resting on a closing tag the opening tag tooltip appears
    /// (<see cref="CodeEditorOpenTagHint"/>); 0–<see cref="OpenTagHintDelayMaxMs"/>, 500 by default.
    /// </summary>
    public int CodeEditorOpenTagHintDelayMs
    {
        get => Math.Clamp(ReadInt("code_editor_open_tag_hint_delay_ms", 500), 0, OpenTagHintDelayMaxMs);
        set => Write("code_editor_open_tag_hint_delay_ms", Math.Clamp(value, 0, OpenTagHintDelayMaxMs));
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

    /// <summary>Preferences → Footnotes: the CSS of Signet's footnote classes (the declarations of each rule).</summary>
    public FootnoteStyles FootnoteStyles
    {
        get
        {
            FootnoteStyles d = FootnoteStyles.Default;
            return new FootnoteStyles(
                ReadString("footnote_style_reference", d.Reference),
                ReadString("footnote_style_note", d.Note),
                ReadString("footnote_style_paragraph", d.Paragraph),
                ReadString("footnote_style_local_container", d.LocalContainer),
                ReadString("footnote_style_global_container", d.GlobalContainer),
                ReadString("footnote_style_separator", d.Separator),
                ReadString("footnote_style_group", d.Group),
                ReadString("footnote_style_group_title", d.GroupTitle));
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Write("footnote_style_reference", value.Reference);
            Write("footnote_style_note", value.Note);
            Write("footnote_style_paragraph", value.Paragraph);
            Write("footnote_style_local_container", value.LocalContainer);
            Write("footnote_style_global_container", value.GlobalContainer);
            Write("footnote_style_separator", value.Separator);
            Write("footnote_style_group", value.Group);
            Write("footnote_style_group_title", value.GroupTitle);
        }
    }

    /// <summary>
    /// Preferences → Footnotes: whether the global notes are grouped by the chapters of their references (off by
    /// default; a notes file grouped this way stays grouped).
    /// </summary>
    public bool FootnoteGroupByChapter
    {
        get => ReadBool("footnote_group_by_chapter", false);
        set => Write("footnote_group_by_chapter", value);
    }

    /// <summary>
    /// Preferences → Footnotes: the letters of the references of a note that several references lead to (a, b by
    /// default). Only notes that become shared from now on take it; shared notes of a book keep their letters.
    /// </summary>
    public FootnoteLetterStyle FootnoteLetterStyle
    {
        get => Enum.TryParse(ReadString("footnote_letter_style", nameof(FootnoteLetterStyle.LowerLatin)), ignoreCase: true, out FootnoteLetterStyle parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : FootnoteLetterStyle.LowerLatin;
        set => Write("footnote_letter_style", value.ToString());
    }

    /// <summary>"Add Footnote": whether the last footnote was added to the global notes (the dialog's default).</summary>
    public bool FootnoteGlobalPlacement
    {
        get => ReadBool("footnote_global_placement", false);
        set => Write("footnote_global_placement", value);
    }

    /// <summary>
    /// Preferences → Footnotes: whether "Remove" in the footnote window asks for a confirmation (enabled by default).
    /// Turned off by "Don't ask again" in that confirmation.
    /// </summary>
    public bool ConfirmFootnoteRemoval
    {
        get => ReadBool("confirm_footnote_removal", true);
        set => Write("confirm_footnote_removal", value);
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

    /// <summary>
    /// Whether a book was open when the application was closed last time. Without one (it was closed after
    /// File → Close) <see cref="ReopenLastFileOnStartup"/> does not reopen the last book.
    /// </summary>
    public bool BookOpenAtExit
    {
        get => ReadBool("book_open_at_exit", true);
        set => Write("book_open_at_exit", value);
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
        get => ReadBool("code_editor_highlight_open_close_tags", true);
        set => Write("code_editor_highlight_open_close_tags", value);
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
    /// <c>0..<see cref="RecentFilesMenuMax"/></c> → 9). The menu shows the first entries of <see cref="RecentFiles"/>.
    /// </summary>
    public int RecentFilesLimit
    {
        get
        {
            int limit = ReadInt("recent_files_limit", RecentFilesDefault);
            return limit is >= 0 and <= RecentFilesMenuMax ? limit : RecentFilesDefault;
        }

        set => Write("recent_files_limit", value);
    }

    /// <summary>
    /// How many recently opened publications to show on the start screen (0 = none; a value outside
    /// <c>0..<see cref="RecentFilesMax"/></c> → 30). The list scrolls when the entries do not fit the window.
    /// </summary>
    public int StartScreenRecentFilesLimit
    {
        get
        {
            int limit = ReadInt("start_screen_recent_files_limit", StartScreenRecentFilesDefault);
            return limit is >= 0 and <= RecentFilesMax ? limit : StartScreenRecentFilesDefault;
        }

        set => Write("start_screen_recent_files_limit", value);
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

    /// <summary>
    /// Whether a Ctrl+Alt stroke that types a character (AltGr+letter) is left to the focused control instead of running a
    /// shortcut ("Enable AltGr on international keyboards"; see <c>AltGrKeys</c> in the application).
    /// </summary>
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
    /// Highlight of the Code Editor caret location in the preview (style, color, opacity, outline,
    /// auto-hide). Always read normalized
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
                ReadInt("preview_highlight_opacity", d.OpacityPercent),
                ReadInt("preview_highlight_outline_width", d.OutlineWidth),
                ReadBool("preview_highlight_auto_hide", d.AutoHide),
                ReadInt("preview_highlight_auto_hide_ms", d.AutoHideDelayMs)).Normalized();
        }

        set
        {
            PreviewHighlight v = value.Normalized();
            WriteSilent("preview_highlight_style", v.Style.ToString());
            WriteSilent("preview_highlight_opacity", v.OpacityPercent);
            WriteSilent("preview_highlight_outline_width", v.OutlineWidth);
            _file.SetRaw(Group, "preview_highlight_auto_hide", v.AutoHide);
            WriteSilent("preview_highlight_auto_hide_ms", v.AutoHideDelayMs);
            RaiseChanged("preview_highlight");
        }
    }

    /// <summary>
    /// Appearance of the warnings (font size of the warning texts in dialogs, warning color). Always read normalized (<see cref="WarningAppearance.Normalized"/>).
    /// </summary>
    public WarningAppearance WarningAppearance
    {
        get
        {
            WarningAppearance d = WarningAppearance.Default;
            return new WarningAppearance(ReadInt("warning_font_size", d.FontSize)).Normalized();
        }

        set
        {
            WarningAppearance v = value.Normalized();
            WriteSilent("warning_font_size", v.FontSize);
            RaiseChanged("warning_appearance");
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

    /// <summary>Code editor appearance settings — font and colors.</summary>
    public CodeEditorAppearance CodeEditorAppearance
    {
        get => ReadCodeEditor("code_editor_", CodeEditorAppearance.Default);
        set
        {
            WriteCodeEditor("code_editor_", value);
            RaiseChanged("code_editor_appearance");
        }
    }

    /// <summary>Font settings of the "Insert Special Character" window.</summary>
    public SpecialCharacterAppearance SpecialCharacterAppearance
    {
        get => new(
            ReadString("special_character_font_family", SpecialCharacterAppearance.Default.FontFamily),
            ReadInt("special_character_font_size", SpecialCharacterAppearance.Default.FontSize),
            FontWeightScale.Normalize(ReadInt("special_character_font_weight", SpecialCharacterAppearance.Default.FontWeight)));

        set
        {
            WriteSilent("special_character_font_family", value.FontFamily);
            WriteSilent("special_character_font_size", value.FontSize);
            WriteSilent("special_character_font_weight", FontWeightScale.Normalize(value.FontWeight));
            RaiseChanged("special_character_appearance");
        }
    }

    /// <summary>The font of the code previews of the Appearance › Color Scheme pages.</summary>
    public ColorSchemePreviewAppearance ColorSchemePreviewAppearance
    {
        get => new(
            ReadString("color_scheme_preview_font_family", ColorSchemePreviewAppearance.Default.FontFamily),
            ReadInt("color_scheme_preview_font_size", ColorSchemePreviewAppearance.Default.FontSize),
            FontWeightScale.Normalize(ReadInt("color_scheme_preview_font_weight", ColorSchemePreviewAppearance.Default.FontWeight)));

        set
        {
            WriteSilent("color_scheme_preview_font_family", value.FontFamily);
            WriteSilent("color_scheme_preview_font_size", value.FontSize);
            WriteSilent("color_scheme_preview_font_weight", FontWeightScale.Normalize(value.FontWeight));
            RaiseChanged("color_scheme_preview_appearance");
        }
    }

    /// <summary>
    /// The identifier of the theme of the interface in use (<see cref="Themes.ThemeStore"/>); Signet Light by default. A
    /// theme that no longer exists is replaced by Signet Light when it is applied.
    /// </summary>
    public string UiThemeId
    {
        get => ReadString("ui_theme_id", Themes.ThemeStore.LightId);
        set
        {
            WriteSilent("ui_theme_id", string.IsNullOrWhiteSpace(value) ? Themes.ThemeStore.LightId : value);
            RaiseChanged("ui_theme_id");
        }
    }

    /// <summary>
    /// The identifier of the color scheme of the editors in use (<see cref="Themes.ThemeStore"/> of the schemes); Signet
    /// Light by default. It is chosen independently of the theme of the interface. A scheme that no longer exists is
    /// replaced by Signet Light when it is applied.
    /// </summary>
    public string EditorSchemeId
    {
        get => ReadString("editor_scheme_id", Themes.ThemeStore.LightId);
        set
        {
            WriteSilent("editor_scheme_id", string.IsNullOrWhiteSpace(value) ? Themes.ThemeStore.LightId : value);
            RaiseChanged("editor_scheme_id");
        }
    }

    /// <summary>The font of the tooltips of an area of the application. Always read normalized.</summary>
    public TooltipFont GetTooltipFont(TooltipArea area) => new TooltipFont(
        ReadString(TooltipFontKey(area, "family"), TooltipFont.Default.FontFamily),
        ReadInt(TooltipFontKey(area, "size"), TooltipFont.Default.FontSize),
        ReadInt(TooltipFontKey(area, "weight"), TooltipFont.Default.FontWeight)).Normalized();

    /// <summary>Sets the font of the tooltips of an area of the application (stored normalized).</summary>
    public void SetTooltipFont(TooltipArea area, TooltipFont value)
    {
        TooltipFont font = value.Normalized();
        WriteSilent(TooltipFontKey(area, "family"), font.FontFamily);
        WriteSilent(TooltipFontKey(area, "size"), font.FontSize);
        WriteSilent(TooltipFontKey(area, "weight"), font.FontWeight);
        RaiseChanged("tooltip_fonts");
    }

    /// <summary>
    /// Look of the Book Browser tree (<see cref="BookBrowserAppearance"/>): fonts, indentation and row spacing. Always
    /// read normalized.
    /// </summary>
    public BookBrowserAppearance BookBrowserAppearance
    {
        get
        {
            BookBrowserAppearance d = BookBrowserAppearance.Default;
            return new BookBrowserAppearance(
                ReadString("book_browser_items_font_family", d.ItemsFontFamily),
                ReadInt("book_browser_items_font_size", d.ItemsFontSize),
                ReadString("book_browser_header_font_family", d.HeaderFontFamily),
                ReadInt("book_browser_header_font_size", d.HeaderFontSize),
                ReadInt("book_browser_indent", d.Indent),
                ReadInt("book_browser_cell_min_height", d.CellMinHeight),
                ReadInt("book_browser_cell_padding", d.CellPadding),
                ReadBool("book_browser_show_folders", d.ShowFolders),
                ReadInt("book_browser_items_font_weight", d.ItemsFontWeight),
                ReadInt("book_browser_header_font_weight", d.HeaderFontWeight)).Normalized();
        }

        set
        {
            BookBrowserAppearance v = value.Normalized();
            WriteSilent("book_browser_items_font_family", v.ItemsFontFamily);
            WriteSilent("book_browser_items_font_size", v.ItemsFontSize);
            WriteSilent("book_browser_header_font_family", v.HeaderFontFamily);
            WriteSilent("book_browser_header_font_size", v.HeaderFontSize);
            WriteSilent("book_browser_indent", v.Indent);
            WriteSilent("book_browser_cell_min_height", v.CellMinHeight);
            WriteSilent("book_browser_cell_padding", v.CellPadding);
            WriteSilent("book_browser_show_folders", v.ShowFolders);
            WriteSilent("book_browser_items_font_weight", v.ItemsFontWeight);
            WriteSilent("book_browser_header_font_weight", v.HeaderFontWeight);
            RaiseChanged("book_browser_appearance");
        }
    }

    /// <summary>
    /// Look of the start screen's list of recent files (<see cref="StartScreenAppearance"/>): fonts and indentation. Always read
    /// normalized.
    /// </summary>
    public StartScreenAppearance StartScreenAppearance
    {
        get
        {
            StartScreenAppearance d = StartScreenAppearance.Default;
            return new StartScreenAppearance(
                ReadString("start_screen_title_font_family", d.TitleFontFamily),
                ReadInt("start_screen_title_font_size", d.TitleFontSize),
                ReadString("start_screen_location_font_family", d.LocationFontFamily),
                ReadInt("start_screen_location_font_size", d.LocationFontSize),
                ReadString("start_screen_section_font_family", d.SectionFontFamily),
                ReadInt("start_screen_section_font_size", d.SectionFontSize),
                ReadInt("start_screen_indent", d.Indent),
                ReadInt("start_screen_title_font_weight", d.TitleFontWeight),
                ReadInt("start_screen_location_font_weight", d.LocationFontWeight),
                ReadInt("start_screen_section_font_weight", d.SectionFontWeight)).Normalized();
        }

        set
        {
            StartScreenAppearance v = value.Normalized();
            WriteSilent("start_screen_title_font_family", v.TitleFontFamily);
            WriteSilent("start_screen_title_font_size", v.TitleFontSize);
            WriteSilent("start_screen_location_font_family", v.LocationFontFamily);
            WriteSilent("start_screen_location_font_size", v.LocationFontSize);
            WriteSilent("start_screen_section_font_family", v.SectionFontFamily);
            WriteSilent("start_screen_section_font_size", v.SectionFontSize);
            WriteSilent("start_screen_indent", v.Indent);
            WriteSilent("start_screen_title_font_weight", v.TitleFontWeight);
            WriteSilent("start_screen_location_font_weight", v.LocationFontWeight);
            WriteSilent("start_screen_section_font_weight", v.SectionFontWeight);
            RaiseChanged("start_screen_appearance");
        }
    }

    /// <summary>Upper bound of <see cref="FindUsagesRowSpacing"/> (the lower bound is 0).</summary>
    public const int FindUsagesRowSpacingMax = 20;

    /// <summary>
    /// The extra space (px) between the rows of the "Find Usages" panel, outside the backgrounds of the cells (so the
    /// background of the panel shows through between them); 0 = the rows touch. Clamped to
    /// 0–<see cref="FindUsagesRowSpacingMax"/>.
    /// </summary>
    public int FindUsagesRowSpacing
    {
        get => Math.Clamp(ReadInt("find_usages_row_spacing", 0), 0, FindUsagesRowSpacingMax);
        set => Write("find_usages_row_spacing", Math.Clamp(value, 0, FindUsagesRowSpacingMax));
    }

    /// <summary>
    /// The fonts of the "Find Usages" panel (<see cref="FindUsagesAppearance"/>): the column headers, the "Context" column
    /// and the other cells. Always read normalized.
    /// </summary>
    public FindUsagesAppearance FindUsagesAppearance
    {
        get
        {
            FindUsagesAppearance d = FindUsagesAppearance.Default;
            return new FindUsagesAppearance(
                ReadString("find_usages_font_header_family", d.HeaderFontFamily),
                ReadInt("find_usages_font_header_size", d.HeaderFontSize),
                ReadString("find_usages_font_context_family", d.ContextFontFamily),
                ReadInt("find_usages_font_context_size", d.ContextFontSize),
                ReadString("find_usages_font_cells_family", d.CellsFontFamily),
                ReadInt("find_usages_font_cells_size", d.CellsFontSize),
                ReadInt("find_usages_font_header_weight", d.HeaderFontWeight),
                ReadInt("find_usages_font_context_weight", d.ContextFontWeight),
                ReadInt("find_usages_font_cells_weight", d.CellsFontWeight)).Normalized();
        }

        set
        {
            FindUsagesAppearance v = value.Normalized();
            WriteSilent("find_usages_font_header_family", v.HeaderFontFamily);
            WriteSilent("find_usages_font_header_size", v.HeaderFontSize);
            WriteSilent("find_usages_font_context_family", v.ContextFontFamily);
            WriteSilent("find_usages_font_context_size", v.ContextFontSize);
            WriteSilent("find_usages_font_cells_family", v.CellsFontFamily);
            WriteSilent("find_usages_font_cells_size", v.CellsFontSize);
            WriteSilent("find_usages_font_header_weight", v.HeaderFontWeight);
            WriteSilent("find_usages_font_context_weight", v.ContextFontWeight);
            WriteSilent("find_usages_font_cells_weight", v.CellsFontWeight);
            RaiseChanged("find_usages_appearance");
        }
    }

    /// <summary>
    /// The fonts of the Saved Searches window (<see cref="SearchEditorAppearance"/>): the "Name" column header and
    /// the rows / Details preview / Name field. Always read normalized.
    /// </summary>
    public SearchEditorAppearance SearchEditorAppearance
    {
        get
        {
            SearchEditorAppearance d = SearchEditorAppearance.Default;
            return new SearchEditorAppearance(
                ReadString("search_editor_font_header_family", d.HeaderFontFamily),
                ReadInt("search_editor_font_header_size", d.HeaderFontSize),
                ReadString("search_editor_font_rows_family", d.RowsFontFamily),
                ReadInt("search_editor_font_rows_size", d.RowsFontSize),
                ReadInt("search_editor_font_header_weight", d.HeaderFontWeight),
                ReadInt("search_editor_font_rows_weight", d.RowsFontWeight)).Normalized();
        }

        set
        {
            SearchEditorAppearance v = value.Normalized();
            WriteSilent("search_editor_font_header_family", v.HeaderFontFamily);
            WriteSilent("search_editor_font_header_size", v.HeaderFontSize);
            WriteSilent("search_editor_font_rows_family", v.RowsFontFamily);
            WriteSilent("search_editor_font_rows_size", v.RowsFontSize);
            WriteSilent("search_editor_font_header_weight", v.HeaderFontWeight);
            WriteSilent("search_editor_font_rows_weight", v.RowsFontWeight);
            RaiseChanged("search_editor_appearance");
        }
    }

    /// <summary>
    /// Whether a Book Browser folder (Text, Styles, …) is expanded when a book is loaded. Only Text by default.
    /// </summary>
    public bool GetBookBrowserExpandOnLoad(OpfModelGroupKind kind) =>
        ReadBool(BookBrowserExpandKey(kind), kind == OpfModelGroupKind.Text);

    /// <summary>Sets <see cref="GetBookBrowserExpandOnLoad"/> of a Book Browser folder.</summary>
    public void SetBookBrowserExpandOnLoad(OpfModelGroupKind kind, bool value) => Write(BookBrowserExpandKey(kind), value);

    // E.g. "book_browser_expand_text".
    private static string BookBrowserExpandKey(OpfModelGroupKind kind) => "book_browser_expand_" + SnakeCase(kind.ToString());

    /// <summary>
    /// Whether the tooltips of an area use the font of <see cref="TooltipArea.OtherWindows"/> instead of their own
    /// (<see cref="GetTooltipFont"/>) — on by default. <see cref="TooltipArea.OtherWindows"/> itself has no parent, so
    /// it never inherits. The own font stays stored while the area inherits, for when the inheritance is turned off.
    /// </summary>
    public bool GetTooltipInherit(TooltipArea area) =>
        area != TooltipArea.OtherWindows && ReadBool(TooltipFontKey(area, "inherit"), true);

    /// <summary>Sets <see cref="GetTooltipInherit"/> of an area (ignored for <see cref="TooltipArea.OtherWindows"/>).</summary>
    public void SetTooltipInherit(TooltipArea area, bool value)
    {
        if (area == TooltipArea.OtherWindows)
        {
            return;
        }

        WriteSilent(TooltipFontKey(area, "inherit"), value);
        RaiseChanged("tooltip_fonts");
    }

    /// <summary>The font the tooltips of an area use now: its own, or — while it inherits — that of the other windows.</summary>
    public TooltipFont GetEffectiveTooltipFont(TooltipArea area) =>
        GetTooltipFont(GetTooltipInherit(area) ? TooltipArea.OtherWindows : area);

    // E.g. "tooltip_find_replace_font_size".
    private static string TooltipFontKey(TooltipArea area, string part) =>
        "tooltip_" + SnakeCase(area.ToString()) + "_font_" + part;

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

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
    /// Resets all appearance settings (Preview / Code Editor / Special Characters)
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
    /// again. Window state, not a preference — written outside the Save / Cancel draft. The key is not
    /// <c>preferences_page</c> (the index in the old section layout, before the Appearance sections got tab bars): an
    /// index from that layout would open some other page, so the window opens on its first page once instead.
    /// </summary>
    public int PreferencesPageIndex
    {
        get => Math.Max(0, ReadIntFrom(MainWindowGroup, "preferences_page_v2", 0));
        set => _file.SetRaw(MainWindowGroup, "preferences_page_v2", Math.Max(0, value));
    }

    /// <summary>
    /// The heights the user gave the resizable text fields of the Preferences window (field name -&gt; height in
    /// pixels). Window state, not a preference — written outside the Save / Cancel draft.
    /// </summary>
    public IReadOnlyDictionary<string, int> PreferencesFieldHeights
    {
        get => ReadSizeMap("preferences_field_heights");
        set => WriteSizeMap("preferences_field_heights", value);
    }

    /// <summary>
    /// The sizes the user gave the split panes of the Preferences window (pane name -&gt; width or height in pixels),
    /// e.g. the width of the tree and the height of the preview of each Appearance › Color Scheme page. Window state,
    /// not a preference — written outside the Save / Cancel draft.
    /// </summary>
    public IReadOnlyDictionary<string, int> PreferencesSplitterSizes
    {
        get => ReadSizeMap("preferences_splitter_sizes");
        set => WriteSizeMap("preferences_splitter_sizes", value);
    }

    /// <summary>
    /// The sections of the Preferences window the user collapsed in the section tree (the <c>x:Name</c> of their pages), so
    /// the tree opens as it was left. Window state, not a preference — written outside the Save / Cancel draft.
    /// </summary>
    public IReadOnlyList<string> PreferencesCollapsedSections
    {
        get => ReadStringListFrom(MainWindowGroup, "preferences_collapsed_sections");
        set => WriteStringListTo(MainWindowGroup, "preferences_collapsed_sections", (value ?? throw new ArgumentNullException(nameof(value))).Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// The zoom of the code editors of the Preferences window, in pixels on top of each editor's own font size (Ctrl +
    /// mouse wheel). Window state, not a preference — written outside the Save / Cancel draft, so it survives a restart
    /// whether or not the changes were saved.
    /// </summary>
    public int PreferencesEditorZoom
    {
        get => Math.Clamp(ReadIntFrom(MainWindowGroup, "preferences_editor_zoom", 0), -12, 48);
        set => _file.SetRaw(MainWindowGroup, "preferences_editor_zoom", Math.Clamp(value, -12, 48));
    }

    /// <summary>
    /// The sizes the user gave the split panes of the other windows (pane name -&gt; width or height in pixels), e.g.
    /// the details of the "Convert Footnotes of Other Origin" dialog. Window state, not a preference.
    /// </summary>
    public IReadOnlyDictionary<string, int> PaneSizes
    {
        get => ReadSizeMap("pane_sizes");
        set => WriteSizeMap("pane_sizes", value);
    }

    // A map of the window group of names to sizes in pixels; entries that are not positive numbers are skipped.
    private Dictionary<string, int> ReadSizeMap(string key)
    {
        Dictionary<string, int> sizes = new(StringComparer.Ordinal);
        if (_file.GetRaw(MainWindowGroup, key) is JsonObject map)
        {
            foreach ((string name, JsonNode? node) in map)
            {
                if (node is JsonValue value && value.TryGetValue(out int size) && size > 0)
                {
                    sizes[name] = size;
                }
            }
        }

        return sizes;
    }

    private void WriteSizeMap(string key, IReadOnlyDictionary<string, int> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        JsonObject map = new();
        foreach ((string name, int size) in sizes.Where(p => p.Value > 0))
        {
            map[name] = size;
        }

        _file.SetRaw(MainWindowGroup, key, map);
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
            LookWhere = ReadLookWhere(),
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

    // The stored scope; All HTML Files when there is none (a fresh installation) or when the value is not a scope (it
    // would leave the "Look in" list without a selection).
    private LookWhere ReadLookWhere()
    {
        int stored = ReadIntFrom(FindReplaceGroup, "look_where", (int)LookWhere.AllHtmlFiles);
        return Enum.IsDefined(typeof(LookWhere), stored) ? (LookWhere)stored : LookWhere.AllHtmlFiles;
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

    private static DockLayoutState? ReadDockLayout(JsonObject obj)
    {
        if (obj["regions"] is not JsonArray regions
            || obj["documents_proportion"] is not JsonValue documents || !documents.TryGetValue(out double documentsProportion))
        {
            return null;
        }

        List<DockRegionState> result = new();
        foreach (JsonObject region in regions.OfType<JsonObject>())
        {
            if (region["dock_id"] is JsonValue id && id.TryGetValue(out string? dockId)
                && region["proportion"] is JsonValue p && p.TryGetValue(out double proportion))
            {
                List<string> tools = (region["tool_ids"] as JsonArray)?
                    .Select(t => t is JsonValue v && v.TryGetValue(out string? s) ? s : null)
                    .OfType<string>()
                    .ToList() ?? new List<string>();
                string? active = region["active_tool_id"] is JsonValue a && a.TryGetValue(out string? activeId) ? activeId : null;
                result.Add(new DockRegionState(dockId, proportion, tools, active));
            }
        }

        return new DockLayoutState(result, documentsProportion);
    }

    private static JsonObject WriteDockLayout(DockLayoutState state) => new()
    {
        ["regions"] = new JsonArray(state.Regions.Select(r => (JsonNode?)new JsonObject
        {
            ["dock_id"] = r.DockId,
            ["proportion"] = r.Proportion,
            ["tool_ids"] = new JsonArray(r.ToolIds.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["active_tool_id"] = r.ActiveToolId,
        }).ToArray()),
        ["documents_proportion"] = state.DocumentsProportion,
    };

    private void WriteSilent(string key, string value) => _file.SetRaw(Group, key, value);

    private void WriteSilent(string key, int value) => _file.SetRaw(Group, key, value);

    private void WriteSilent(string key, bool value) => _file.SetRaw(Group, key, value);

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

    private CodeEditorAppearance ReadCodeEditor(string prefix, CodeEditorAppearance defaults) => new()
    {
        FontFamily = ReadString(prefix + "font_family_standard", defaults.FontFamily),
        FontSize = ReadInt(prefix + "font_size", defaults.FontSize),
        FontWeight = FontWeightScale.Normalize(ReadInt(prefix + "font_weight", defaults.FontWeight)),
    };

    private void WriteCodeEditor(string prefix, CodeEditorAppearance a)
    {
        WriteSilent(prefix + "font_family_standard", a.FontFamily);
        WriteSilent(prefix + "font_size", a.FontSize);
        WriteSilent(prefix + "font_weight", FontWeightScale.Normalize(a.FontWeight));
    }

    private static IEnumerable<string> AppearanceKeys()
    {
        yield return "show_zoom_hint";
        yield return "window_title_full_path";
        yield return "preview_font_family_standard";
        yield return "preview_font_family_serif";
        yield return "preview_font_family_sans_serif";
        yield return "preview_font_size";
        yield return "special_character_font_family";
        yield return "special_character_font_size";
        yield return "special_character_font_weight";
        yield return "warning_font_size";
        yield return "open_tag_hint_font_family";
        yield return "open_tag_hint_font_size";
        yield return "open_tag_hint_font_weight";
        yield return "book_browser_items_font_family";
        yield return "book_browser_items_font_size";
        yield return "book_browser_items_font_weight";
        yield return "book_browser_header_font_family";
        yield return "book_browser_header_font_size";
        yield return "book_browser_header_font_weight";
        yield return "book_browser_indent";
        yield return "book_browser_cell_min_height";
        yield return "book_browser_cell_padding";
        yield return "book_browser_show_folders";
        yield return "start_screen_title_font_family";
        yield return "start_screen_title_font_size";
        yield return "start_screen_title_font_weight";
        yield return "start_screen_location_font_family";
        yield return "start_screen_location_font_size";
        yield return "start_screen_location_font_weight";
        yield return "start_screen_section_font_family";
        yield return "start_screen_section_font_size";
        yield return "start_screen_section_font_weight";
        yield return "start_screen_indent";
        yield return "find_usages_fit_usage_column";
        yield return "find_usages_row_spacing";
        yield return "find_usages_font_header_family";
        yield return "find_usages_font_header_size";
        yield return "find_usages_font_header_weight";
        yield return "find_usages_font_context_family";
        yield return "find_usages_font_context_size";
        yield return "find_usages_font_context_weight";
        yield return "find_usages_font_cells_family";
        yield return "find_usages_font_cells_size";
        yield return "find_usages_font_cells_weight";
        yield return "search_editor_font_header_family";
        yield return "search_editor_font_header_size";
        yield return "search_editor_font_header_weight";
        yield return "search_editor_font_rows_family";
        yield return "search_editor_font_rows_size";
        yield return "search_editor_font_rows_weight";
        foreach (TooltipArea area in Enum.GetValues<TooltipArea>())
        {
            yield return TooltipFontKey(area, "family");
            yield return TooltipFontKey(area, "size");
            yield return TooltipFontKey(area, "weight");
            yield return TooltipFontKey(area, "inherit");
        }

        yield return "preferences_window_modal";
        foreach (OpfModelGroupKind kind in Enum.GetValues<OpfModelGroupKind>())
        {
            yield return BookBrowserExpandKey(kind);
        }

        yield return "code_editor_font_family_standard";
        yield return "code_editor_font_size";
        yield return "code_editor_font_weight";
        yield return "code_editor_line_numbers_font_family";
        yield return "code_editor_line_numbers_font_size";
        yield return "code_editor_line_numbers_font_weight";
        yield return "document_tab_font_family";
        yield return "document_tab_font_size";
        yield return "document_tab_font_weight";
        yield return "document_tab_font_inherit_tool_header";
        yield return "tool_header_font_family";
        yield return "tool_header_font_size";
        yield return "tool_header_font_weight";
        yield return "notifications_timestamp_font_family";
        yield return "notifications_timestamp_font_size";
        yield return "notifications_timestamp_font_weight";
        yield return "notifications_message_font_family";
        yield return "notifications_message_font_size";
        yield return "notifications_message_font_weight";
        yield return "notifications_entry_spacing";
        yield return "notifications_timestamp_font_inherit_tool_header";
        yield return "notifications_message_font_inherit_tool_header";
    }

    // The colors the settings held before the themes (by their old keys).
    private static readonly string[] LegacyColorKeys =
    {
            "code_editor_css_comment_color",
            "code_editor_css_property_color",
            "code_editor_css_quote_color",
            "code_editor_css_selector_color",
            "code_editor_css_value_color",
            "code_editor_css_special_selector_color",
            "code_editor_css_at_rule_color",
            "code_editor_css_constant_color",
            "code_editor_line_highlight_color",
            "code_editor_xhtml_attribute_name_color",
            "code_editor_xhtml_attribute_value_color",
            "code_editor_xhtml_css_color",
            "code_editor_xhtml_css_comment_color",
            "code_editor_xhtml_doctype_color",
            "code_editor_xhtml_entity_color",
            "code_editor_xhtml_html_color",
            "code_editor_xhtml_html_comment_color",
            "code_editor_xhtml_namespace_prefix_color",
            "code_editor_link_color",
            "code_editor_error_underline_color",
            "code_editor_search_match_background_color",
            "code_editor_selection_background_color",
            "warning_color_light",
            "error_color_light",
            "open_tag_hint_light_background",
            "open_tag_hint_light_foreground",
            "preview_highlight_color_light",
    };

    // The keys of settings that were removed — the dark application theme (the application has one, light theme until
    // configurable themes come) and Code Editor colors nothing used: dropped on load, so the next save no longer writes
    // them.
    private void RemoveObsoleteKeys()
    {
        // The colors of before the themes: the themes and the color schemes start from scratch, so they are dropped.
        foreach (string key in LegacyColorKeys)
        {
            _file.Remove(Group, key);
        }

        string[] keys =
        {
            // The row spacing of the Book Browser tree (the minimum height and the padding of a cell do the same) and the
            // old, never configurable "show full path".
            "book_browser_row_spacing", "showfullpath_on",
            "ui_theme", "ui_custom_signet_dark_theme", "preview_dark_in_dm", "preview_highlight_color_dark",
            "warning_color_dark", "error_color_dark", "open_tag_hint_dark_background", "open_tag_hint_dark_foreground",
            // Code Editor colors nothing used: the line numbers and the spelling squiggle had fixed colors.
            "code_editor_line_number_background_color", "code_editor_line_number_foreground_color",
            "code_editor_spelling_underline_color",
            // The clipboard history is kept for the session only: what was copied does not stay on the disk.
            "clipboardhistory",
        };
        foreach (string key in keys)
        {
            _file.Remove(Group, key);
        }

        foreach ((string group, string key) in _file.Entries().ToList())
        {
            if (group == Group && key.StartsWith("cv_dark_", StringComparison.Ordinal))
            {
                _file.Remove(group, key);
            }
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
