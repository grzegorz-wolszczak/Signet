using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>A theme or a color scheme in the list of its page.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Name">The name.</param>
/// <param name="IsBundled">Whether the theme comes with Signet.</param>
public sealed record ThemeOption(string Id, string Name, bool IsBundled)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The management of the themes of the interface (Preferences › Appearance and Behavior › Appearance) or of the color
/// schemes of the editors (Preferences › Appearance and Behavior › Code Editor › Color Scheme), as Rider's pages of the
/// same names: the one in use (chosen in the draft of the window, applied on Save), the actions on the list (IntelliJ's
/// <c>AbstractSchemeActions</c>) and, for a theme, its settings as a whole (the variant of the controls, the icon set).
/// The two lists are independent: each has its own store, files and selection.
/// <para>Changes are kept in a copy until Save (<see cref="Apply"/>); Duplicate, Rename, Delete, Restore Defaults and
/// Import act on the files at once, as file operations.</para>
/// </summary>
public sealed partial class ThemeDocumentViewModel : ObservableObject
{
    private readonly ThemeStore _store;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, Theme> _edited = new(StringComparer.Ordinal);

    // Resolves the keys with the changes not saved yet, also those of the parents.
    private readonly ThemeResolver _editResolver;

    /// <summary>Creates the page.</summary>
    /// <param name="store">The themes or the color schemes (<see cref="ThemeStore.Kind"/>).</param>
    /// <param name="settings">The draft of the Preferences window (the one in use, <see cref="SettingsStore.UiThemeId"/> or
    /// <see cref="SettingsStore.EditorSchemeId"/>).</param>
    internal ThemeDocumentViewModel(ThemeStore store, SettingsStore settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _editResolver = new ThemeResolver(id => _edited.GetValueOrDefault(id) ?? _store.Find(id));
        Pages = Enum.GetValues<ThemePage>()
            .Where(page => ThemeKeys.On(page).Any(k => k.Document == Kind))
            .Select(page => new ThemeKeysPageViewModel(this, page))
            .ToArray();
        Variants = new[]
        {
            new VariantOption(InterfaceVariant.Light, Strings.Get("ColorScheme_VariantLight")),
            new VariantOption(InterfaceVariant.Dark, Strings.Get("ColorScheme_VariantDark")),
        };
        RefreshThemes(Kind == ThemeDocumentKind.Theme ? _settings.UiThemeId : _settings.EditorSchemeId);
    }

    /// <summary>Resolves the keys with the changes not saved yet (also those of the parents).</summary>
    internal ThemeResolver EditResolver => _editResolver;

    /// <summary>The label of the list: "Theme:" or "Editor color scheme:".</summary>
    public string Label => Str("Label");

    /// <summary>The tooltip of the gear next to the list.</summary>
    public string ActionsTip => Str("Actions");

    /// <summary>The note of a bundled theme or scheme: changes are kept beside it, it cannot be renamed or deleted.</summary>
    public string BundledNote => Str("BundledNote");

    /// <summary>The title of the window that asks where to export to.</summary>
    public string ExportTitle => Str("ExportTitle");

    /// <summary>The title of the window that asks for a file to import.</summary>
    public string ImportTitle => Str("ImportTitle");

    /// <summary>The name of the file type in the pickers.</summary>
    public string FileTypeName => Str("FileType");

    /// <summary>Whether this manages the themes of the interface or the color schemes of the editors.</summary>
    public ThemeDocumentKind Kind => _store.Kind;

    /// <summary>Raised when the changes not saved yet appear or go (the Save button of the window).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the values of the keys may differ: another theme was selected, or a key was changed.</summary>
    public event EventHandler? ThemeEdited;

    /// <summary>
    /// Raised when the one in use was changed by the user (not when the list was rebuilt): Preferences switches the color
    /// scheme with the theme.
    /// </summary>
    public event EventHandler? SelectionChangedByUser;

    /// <summary>Raised by a link to a key on another page ("Inherit values from …"): the window shows that page.</summary>
    public event EventHandler<ThemeKey>? KeyRequested;

    /// <summary>
    /// Picks a color: title, initial color (<c>#RRGGBB</c> / <c>#AARRGGBB</c>) → the color, or <c>null</c> after
    /// Cancel; <c>null</c> (tests without a UI) = the pick buttons do nothing.
    /// </summary>
    public Func<string, string, Task<string?>>? ColorPicker { get; set; }

    /// <summary>The pages of the keys (General, XHTML, CSS, …; for a theme only Interface), in the order of <see cref="ThemePage"/>.</summary>
    public IReadOnlyList<ThemeKeysPageViewModel> Pages { get; }

    /// <summary>The page of the keys of <paramref name="page"/>.</summary>
    /// <exception cref="InvalidOperationException">The page belongs to the other document.</exception>
    public ThemeKeysPageViewModel PageFor(ThemePage page) =>
        Pages.FirstOrDefault(p => p.Page == page)
        ?? throw new InvalidOperationException($"The page {page} belongs to the {(Kind == ThemeDocumentKind.Theme ? "color scheme" : "theme")}.");

    /// <summary>Whether the page is edited here (the keys of the interface in the theme, the others in the scheme).</summary>
    public bool Edits(ThemePage page) => Pages.Any(p => p.Page == page);

    /// <summary>Whether Code Editor uses the extended highlighting (in the draft of the window) — the previews follow it.</summary>
    public bool ExtendedHighlighting => _settings.CodeEditorExtendedHighlighting;

    /// <summary>The font of the code previews of the pages (in the draft of the window, like every setting).</summary>
    public ColorSchemePreviewAppearance PreviewFont
    {
        get => _settings.ColorSchemePreviewAppearance;
        set
        {
            if (value != PreviewFont)
            {
                _settings.ColorSchemePreviewAppearance = value;
                OnPropertyChanged();
                foreach (ThemeKeysPageViewModel page in Pages)
                {
                    page.RefreshPreviewFont();
                }
            }
        }
    }

    /// <summary>
    /// The selected theme or scheme with the changes not saved yet, resolved (also with the changes of its parents); the
    /// other document is Signet Light.
    /// </summary>
    public ResolvedTheme? Resolved => Current is { } theme ? ResolvedTheme.Of(Kind, theme, _editResolver) : null;

    /// <summary>Asks for a name: title, prompt, initial text → the text, or <c>null</c> after Cancel.</summary>
    public Func<string, string, string, Task<string?>>? TextPrompt { get; set; }

    /// <summary>Asks to confirm a destructive action: title, message → whether confirmed.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Shows a message: title, text.</summary>
    public Func<string, string, Task>? Message { get; set; }

    /// <summary>Asks where to export: the suggested file name → a path, or <c>null</c>.</summary>
    public Func<string, Task<string?>>? PickExportPath { get; set; }

    /// <summary>Asks for a theme file to import → a path, or <c>null</c>.</summary>
    public Func<Task<string?>>? PickImportPath { get; set; }

    /// <summary>The themes: the bundled ones first, then the user's by name.</summary>
    public ObservableCollection<ThemeOption> Themes { get; } = new();

    /// <summary>The variants of the controls.</summary>
    public IReadOnlyList<VariantOption> Variants { get; }

    /// <summary>The theme in use (in the draft; applied on Save) and edited on the page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variant), nameof(IsBundled), nameof(CanRestoreDefaults))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(RestoreDefaultsCommand))]
    private ThemeOption? _selected;

    partial void OnSelectedChanged(ThemeOption? oldValue, ThemeOption? newValue)
    {
        if (oldValue is not null && newValue is not null)
        {
            SelectionChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedChanged(ThemeOption? value)
    {
        if (value is not null)
        {
            if (Kind == ThemeDocumentKind.Theme)
            {
                _settings.UiThemeId = value.Id;
            }
            else
            {
                _settings.EditorSchemeId = value.Id;
            }
        }

        ThemeEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects a bundled theme or scheme (by <see cref="ThemeStore.LightId"/> / <see cref="ThemeStore.DarkId"/>) without
    /// telling that the user did it.
    /// </summary>
    internal void SelectBundled(string id) => Selected = Themes.FirstOrDefault(t => t.Id == id) ?? Selected;

    /// <summary>The variant of the controls of the selected theme; light for a scheme.</summary>
    internal InterfaceVariant CurrentVariant => Current is { } theme ? _store.Resolver.Variant(theme) : InterfaceVariant.Light;
    /// <summary>Whether the selected theme changes the key (<see cref="ThemeStore.ChangesKey"/>).</summary>
    internal bool ChangesKey(ThemeKey key) => Current is { } theme && _store.ChangesKey(theme, key);

    /// <summary>
    /// Where "restore" takes the value of a changed key from: the name of the parent of a theme of the user;
    /// <c>null</c> for a bundled theme (its own value).
    /// </summary>
    internal string? RestoreSource =>
        Current is { ParentId: { } parentId } ? (_edited.GetValueOrDefault(parentId) ?? _store.Find(parentId))?.Name : null;

    /// <summary>Gives a key of the selected theme a value (kept until Save).</summary>
    internal void SetKey(ThemeKey key, ThemeEntry entry)
    {
        if (Current is { } theme && !Equals(theme.Entries.GetValueOrDefault(key.Id), entry))
        {
            Edit(theme.With(key.Id, entry));
        }
    }

    /// <summary>Undoes the change of a key of the selected theme (<see cref="ThemeStore.WithoutChange"/>).</summary>
    internal void RestoreKey(ThemeKey key)
    {
        if (Current is { } theme)
        {
            Edit(_store.WithoutChange(theme, key));
        }
    }

    /// <summary>Shows the key on its page (a link to a key, e.g. the base of an inherited one).</summary>
    internal void RequestKey(ThemeKey key)
    {
        PageFor(key.Page).SelectKey(key);
        KeyRequested?.Invoke(this, key);
    }

    /// <summary>The selected theme with the changes not saved yet.</summary>
    public Theme? Current => Selected is null ? null : _edited.GetValueOrDefault(Selected.Id) ?? _store.Find(Selected.Id);

    /// <summary>Whether the selected theme comes with Signet (it cannot be renamed or deleted).</summary>
    public bool IsBundled => Selected?.IsBundled ?? false;

    /// <summary>Whether "Restore Defaults" has something to undo: a bundled theme with changes, saved or not.</summary>
    public bool CanRestoreDefaults => Selected is { IsBundled: true } s && (_store.IsModified(s.Id) || _edited.ContainsKey(s.Id));

    /// <summary>Whether there are changes not saved yet.</summary>
    public bool HasChanges => _edited.Count > 0;

    /// <summary>The variant of the controls of the selected theme.</summary>
    public VariantOption? Variant
    {
        get => Kind == ThemeDocumentKind.Theme && Current is { } theme ? Variants.Single(v => v.Value == _store.Resolver.Variant(theme)) : null;
        set
        {
            if (value is not null && Current is { } theme && _store.Resolver.Variant(theme) != value.Value)
            {
                Edit(theme with { Variant = value.Value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Saves the changed themes (Save of the window); returns whether there were any.</summary>
    public bool Apply()
    {
        if (_edited.Count == 0)
        {
            return false;
        }

        foreach (Theme theme in _edited.Values)
        {
            _store.Save(theme);
        }

        _edited.Clear();
        RefreshThemes(Selected?.Id);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>"Duplicate…": a new theme of the user with the values of the selected one (also those not saved yet).</summary>
    [RelayCommand]
    private async Task Duplicate()
    {
        if (Selected is not { } source || Current is not { } current
            || await AskName("DuplicateTitle", _store.UniqueName(source.Name)) is not { } name)
        {
            return;
        }

        Theme copy = _store.Duplicate(source.Id, name, _edited.GetValueOrDefault(source.Id) ?? current);
        RefreshThemes(copy.Id);
    }

    /// <summary>"Rename…" the selected theme of the user.</summary>
    [RelayCommand(CanExecute = nameof(IsUserTheme))]
    private async Task Rename()
    {
        if (Selected is not { IsBundled: false } theme
            || await AskName("RenameTitle", theme.Name, theme.Id) is not { } name)
        {
            return;
        }

        _store.Rename(theme.Id, name);
        if (_edited.TryGetValue(theme.Id, out Theme? edited))
        {
            _edited[theme.Id] = edited with { Name = name.Trim() };
        }

        RefreshThemes(theme.Id);
    }

    /// <summary>"Delete" the selected theme of the user (after a confirmation); its parent becomes the selected one.</summary>
    [RelayCommand(CanExecute = nameof(IsUserTheme))]
    private async Task Delete()
    {
        if (Selected is not { IsBundled: false } theme || Current is not { } current
            || Confirm is null
            || !await Confirm(Str("DeleteTitle"), StrFormat("DeleteConfirm", theme.Name)))
        {
            return;
        }

        string next = current.ParentId ?? ThemeStore.LightId;
        _store.Delete(theme.Id);
        bool hadChanges = _edited.Remove(theme.Id);
        RefreshThemes(next);
        if (hadChanges)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"Restore Defaults" of the selected bundled theme: its changes, saved or not, are removed.</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreDefaults))]
    private void RestoreDefaults()
    {
        if (Selected is not { IsBundled: true } theme)
        {
            return;
        }

        _store.RestoreDefaults(theme.Id);
        bool hadChanges = _edited.Remove(theme.Id);
        RefreshThemes(theme.Id);
        if (hadChanges)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"Export…" the selected theme (with the changes not saved yet) to a file.</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (Selected is not { } theme || PickExportPath is null
            || await PickExportPath(theme.Name + ThemeXml.ExtensionOf(Kind)) is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            using StreamWriter writer = new(path, false, new UTF8Encoding(false));
            _store.Export(theme.Id, writer, AppVersion.Text, _edited.GetValueOrDefault(theme.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Show(Str("ExportTitle"), StrFormat("ExportFailed", e.Message));
        }
    }

    /// <summary>"Import…" a theme file; the imported theme becomes the selected one.</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (PickImportPath is null || await PickImportPath() is not { Length: > 0 } path)
        {
            return;
        }

        List<string> warnings = new();
        try
        {
            Theme imported;
            using (StreamReader reader = new(path, Encoding.UTF8))
            {
                imported = _store.Import(reader, warnings);
            }

            RefreshThemes(imported.Id);
            if (warnings.Count > 0)
            {
                await Show(Str("ImportTitle"),
                    StrFormat("ImportWarnings", imported.Name, string.Join("\n", warnings)));
            }
        }
        catch (Exception e) when (e is ThemeFormatException or IOException or UnauthorizedAccessException)
        {
            await Show(Str("ImportTitle"), StrFormat("ImportFailed", e.Message));
        }
    }

    // The text of this kind of document: "Theme_<name>" for a theme, "Scheme_<name>" for a color scheme.
    private string Str(string name) => Strings.Get((Kind == ThemeDocumentKind.Theme ? "Theme_" : "Scheme_") + name);

    private string StrFormat(string name, params object[] args) =>
        Strings.Format((Kind == ThemeDocumentKind.Theme ? "Theme_" : "Scheme_") + name, args);

    private bool IsUserTheme() => Selected is { IsBundled: false };

    // Keeps a changed copy of a theme until Save.
    private void Edit(Theme theme)
    {
        bool first = _edited.Count == 0;
        _edited[theme.Id] = theme;
        OnPropertyChanged(nameof(CanRestoreDefaults));
        RestoreDefaultsCommand.NotifyCanExecuteChanged();
        ThemeEdited?.Invoke(this, EventArgs.Empty);
        if (first)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Asks for a name that no other theme has; null when the user cancels or gives a taken name (they are told).
    private async Task<string?> AskName(string titleKey, string initial, string? exceptId = null)
    {
        if (TextPrompt is null
            || await TextPrompt(Str(titleKey), Str("NamePrompt"), initial) is not { } name
            || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_store.IsNameTaken(name, exceptId))
        {
            await Show(Str(titleKey), StrFormat("NameTaken", name.Trim()));
            return null;
        }

        return name.Trim();
    }

    private Task Show(string title, string text) => Message?.Invoke(title, text) ?? Task.CompletedTask;

    // Rebuilds the list (names may have changed) and selects the theme (Signet Light when it is gone).
    private void RefreshThemes(string? selectId)
    {
        Themes.Clear();
        foreach (Theme theme in _store.Themes)
        {
            Themes.Add(new ThemeOption(theme.Id, theme.Name, ThemeStore.IsBundled(theme.Id)));
        }

        ThemeOption? selected = Themes.FirstOrDefault(t => t.Id == selectId) ?? Themes.First(t => t.Id == ThemeStore.LightId);
        if (Selected == selected)
        {
            // The same option (a record: equal by value) — still refresh what depends on the theme.
            OnSelectedChanged(selected);
            OnPropertyChanged(string.Empty);
            RestoreDefaultsCommand.NotifyCanExecuteChanged();
        }

        Selected = selected;
    }
}

/// <summary>A variant of the controls in the list of Appearance › Color Scheme.</summary>
/// <param name="Value">The variant.</param>
/// <param name="DisplayName">The name in the list.</param>
public sealed record VariantOption(InterfaceVariant Value, string DisplayName);
