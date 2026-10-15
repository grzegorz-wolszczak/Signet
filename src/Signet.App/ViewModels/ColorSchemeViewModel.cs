using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

/// <summary>A theme in the list of Appearance › Color Scheme.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Name">The name.</param>
/// <param name="IsBundled">Whether the theme comes with Signet.</param>
public sealed record ThemeOption(string Id, string Name, bool IsBundled)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>A choice of the icon set of a theme: a code, or <c>null</c> for the one chosen in Appearance.</summary>
/// <param name="Code">The code of the icon set (<see cref="IconThemeManager.SupportedThemes"/>); <c>null</c> = Appearance's.</param>
/// <param name="DisplayName">The name in the list.</param>
public sealed record IconSetOption(string? Code, string DisplayName);

/// <summary>
/// Preferences › Appearance › Color Scheme, as Rider's Editor › Color Scheme: the theme in use (chosen in the draft of
/// the window, applied on Save), the actions on themes (IntelliJ's <c>AbstractSchemeActions</c>) and the settings of the
/// theme as a whole (the variant of the controls, the icon set).
/// <para>Changes to a theme are kept in a copy until Save (<see cref="Apply"/>); Duplicate, Rename, Delete, Restore
/// Defaults and Import act on the theme files at once, as file operations.</para>
/// </summary>
public sealed partial class ColorSchemeViewModel : ObservableObject
{
    private readonly ThemeStore _store;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, Theme> _edited = new(StringComparer.Ordinal);

    // Resolves the keys with the changes not saved yet, also those of the parents.
    private readonly ThemeResolver _editResolver;

    /// <summary>Creates the page.</summary>
    /// <param name="store">The themes.</param>
    /// <param name="settings">The draft of the Preferences window (the theme in use, <see cref="SettingsStore.UiThemeId"/>).</param>
    internal ColorSchemeViewModel(ThemeStore store, SettingsStore settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _editResolver = new ThemeResolver(id => _edited.GetValueOrDefault(id) ?? _store.Find(id));
        Pages = Enum.GetValues<ThemePage>().Select(page => new ColorSchemePageViewModel(this, page)).ToArray();
        Variants = new[]
        {
            new VariantOption(InterfaceVariant.Light, Strings.Get("ColorScheme_VariantLight")),
            new VariantOption(InterfaceVariant.Dark, Strings.Get("ColorScheme_VariantDark")),
        };
        IconSets = new[] { new IconSetOption(null, Strings.Get("ColorScheme_IconSetFromAppearance")) }
            .Concat(IconThemeManager.SupportedThemes.Select(t => new IconSetOption(t.Code, t.DisplayName)))
            .ToArray();
        RefreshThemes(_settings.UiThemeId);
    }

    /// <summary>Raised when the changes not saved yet appear or go (the Save button of the window).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the values of the keys may differ: another theme was selected, or a key was changed.</summary>
    public event EventHandler? ThemeEdited;

    /// <summary>Raised by a link to a key on another page ("Inherit values from …"): the window shows that page.</summary>
    public event EventHandler<ThemeKey>? KeyRequested;

    /// <summary>
    /// Picks a color: title, initial color (<c>#RRGGBB</c> / <c>#AARRGGBB</c>) → the color, or <c>null</c> after
    /// Cancel; <c>null</c> (tests without a UI) = the pick buttons do nothing.
    /// </summary>
    public Func<string, string, Task<string?>>? ColorPicker { get; set; }

    /// <summary>The pages of the keys (General, XHTML, CSS, …), in the order of <see cref="ThemePage"/>.</summary>
    public IReadOnlyList<ColorSchemePageViewModel> Pages { get; }

    /// <summary>The page of the keys of <paramref name="page"/>.</summary>
    public ColorSchemePageViewModel PageFor(ThemePage page) => Pages[(int)page];

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
                foreach (ColorSchemePageViewModel page in Pages)
                {
                    page.RefreshPreviewFont();
                }
            }
        }
    }

    /// <summary>The selected theme with the changes not saved yet, resolved (also with the changes of its parents).</summary>
    public ResolvedTheme? Resolved => Current is { } theme ? new ResolvedTheme(theme, _editResolver) : null;

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

    /// <summary>The icon sets a theme can name.</summary>
    public IReadOnlyList<IconSetOption> IconSets { get; }

    /// <summary>The theme in use (in the draft; applied on Save) and edited on the page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variant), nameof(IconSet), nameof(IsBundled), nameof(CanRestoreDefaults), nameof(UseThemeFonts))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(RestoreDefaultsCommand))]
    private ThemeOption? _selected;

    partial void OnSelectedChanged(ThemeOption? value)
    {
        if (value is not null)
        {
            _settings.UiThemeId = value.Id;
        }

        ThemeEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the selected theme changes the key (<see cref="ThemeStore.ChangesKey"/>).</summary>
    internal bool ChangesKey(ThemeKey key) => Current is { } theme && _store.ChangesKey(theme, key);

    /// <summary>
    /// Where "restore" takes the value of a changed key from: the name of the parent of a theme of the user;
    /// <c>null</c> for a bundled theme (its own value).
    /// </summary>
    internal string? RestoreSource =>
        Current is { ParentId: { } parentId } ? (_edited.GetValueOrDefault(parentId) ?? _store.Find(parentId))?.Name : null;

    /// <summary>
    /// "Use the theme fonts instead of the defaults" (Rider's color scheme font): turned on, the theme gets a copy of the
    /// fonts of the settings for every place it does not set yet, so nothing changes until a font is edited; turned off,
    /// the theme keeps its fonts but the settings' are used.
    /// </summary>
    public bool UseThemeFonts
    {
        get => Current?.UseThemeFonts ?? false;
        set
        {
            if (Current is not { } theme || theme.UseThemeFonts == value)
            {
                return;
            }

            ImmutableDictionary<string, ThemeFont> fonts = theme.Fonts;
            if (value)
            {
                foreach ((string key, ThemeFont font) in ThemeFontKeys.FromSettings(_settings))
                {
                    if (_editResolver.Font(theme, key) is null)
                    {
                        fonts = fonts.SetItem(key, font);
                    }
                }
            }

            Edit(theme with { UseThemeFonts = value, Fonts = fonts });
            OnPropertyChanged();
        }
    }

    /// <summary>The font of a place in the selected theme (its parents' too), otherwise the one of the settings.</summary>
    public ThemeFont FontOf(string key) =>
        (Current is { } theme ? _editResolver.Font(theme, key) : null) ?? ThemeFontKeys.FromSettings(_settings)[key];

    /// <summary>Gives a place of the selected theme a font (kept until Save).</summary>
    public void SetFont(string key, ThemeFont font)
    {
        if (Current is { } theme && FontOf(key) != font)
        {
            Edit(theme with { Fonts = theme.Fonts.SetItem(key, font) });
        }
    }

    /// <summary>"Restore defaults" of a font of the theme: the font of the settings, copied again.</summary>
    public void RestoreFont(string key) => SetFont(key, ThemeFontKeys.FromSettings(_settings)[key]);

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
        get => Current is { } theme ? Variants.Single(v => v.Value == _store.Resolver.Variant(theme)) : null;
        set
        {
            if (value is not null && Current is { } theme && _store.Resolver.Variant(theme) != value.Value)
            {
                Edit(theme with { Variant = value.Value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The icon set of the selected theme (<c>null</c> code = the one chosen in Appearance).</summary>
    public IconSetOption? IconSet
    {
        get => Current is { } theme ? IconSets.FirstOrDefault(o => o.Code == _store.Resolver.IconSet(theme)) ?? IconSets[0] : null;
        set
        {
            if (value is not null && Current is { } theme && _store.Resolver.IconSet(theme) != value.Code)
            {
                Edit(theme with { IconSet = value.Code });
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
            || await AskName("ColorScheme_DuplicateTitle", _store.UniqueName(source.Name)) is not { } name)
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
            || await AskName("ColorScheme_RenameTitle", theme.Name, theme.Id) is not { } name)
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
            || !await Confirm(Strings.Get("ColorScheme_DeleteTitle"), Strings.Format("ColorScheme_DeleteConfirm", theme.Name)))
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
            || await PickExportPath(theme.Name + ThemeXml.Extension) is not { Length: > 0 } path)
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
            await Show(Strings.Get("ColorScheme_ExportTitle"), Strings.Format("ColorScheme_ExportFailed", e.Message));
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
                await Show(Strings.Get("ColorScheme_ImportTitle"),
                    Strings.Format("ColorScheme_ImportWarnings", imported.Name, string.Join("\n", warnings)));
            }
        }
        catch (Exception e) when (e is ThemeFormatException or IOException or UnauthorizedAccessException)
        {
            await Show(Strings.Get("ColorScheme_ImportTitle"), Strings.Format("ColorScheme_ImportFailed", e.Message));
        }
    }

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
            || await TextPrompt(Strings.Get(titleKey), Strings.Get("ColorScheme_NamePrompt"), initial) is not { } name
            || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_store.IsNameTaken(name, exceptId))
        {
            await Show(Strings.Get(titleKey), Strings.Format("ColorScheme_NameTaken", name.Trim()));
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
