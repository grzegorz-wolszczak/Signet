using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>An element of the tree of a page of the theme editor: a group, or a key (IntelliJ's <c>Group//Item</c>).</summary>
public sealed partial class ThemeKeyNode : ObservableObject
{
    /// <summary>Creates a node.</summary>
    /// <param name="name">The name in the tree.</param>
    /// <param name="key">The key; <c>null</c> for a group.</param>
    public ThemeKeyNode(string name, ThemeKey? key)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Key = key;
    }

    /// <summary>The name in the tree.</summary>
    public string Name { get; }

    /// <summary>The key; <c>null</c> for a group.</summary>
    public ThemeKey? Key { get; }

    /// <summary>Whether the node is a group.</summary>
    public bool IsGroup => Key is null;

    /// <summary>The keys of a group.</summary>
    public ObservableCollection<ThemeKeyNode> Children { get; } = new();

    /// <summary>Whether the keys of a group are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Whether the theme changes the key (or a key of the group) — shown in bold, as Rider shows changed settings.</summary>
    [ObservableProperty]
    private bool _isModified;
}

/// <summary>A kind of effect in the list of the attribute panel.</summary>
/// <param name="Value">The effect.</param>
/// <param name="DisplayName">The name in the list.</param>
public sealed record EffectOption(EffectType Value, string DisplayName);

/// <summary>
/// A page of the keys of Appearance › Color Scheme (General, XHTML, …), as Rider's Color Scheme pages: the tree of the
/// keys of the page on the left and the panel of the selected key on the right — Bold / Italic, Foreground /
/// Background, the effect, "Inherit values from …" for a key with a base, or a single color for a color key. The
/// changes go to <see cref="ColorSchemeViewModel"/>, kept there until Save.
/// </summary>
public sealed partial class ColorSchemePageViewModel : ObservableObject
{
    private readonly ColorSchemeViewModel _owner;
    private readonly List<ThemeKeyNode> _keyNodes = new();

    /// <summary>Creates the page.</summary>
    /// <param name="owner">The theme being edited.</param>
    /// <param name="page">Which page.</param>
    internal ColorSchemePageViewModel(ColorSchemeViewModel owner, ThemePage page)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Page = page;
        Effects = Enum.GetValues<EffectType>().Where(e => e != EffectType.None)
            .Select(e => new EffectOption(e, Strings.Get("ColorScheme_Effect" + e))).ToArray();
        foreach (IGrouping<string, ThemeKey> group in ThemeKeys.On(page).GroupBy(k => k.Group))
        {
            if (group.Key.Length == 0)
            {
                foreach (ThemeKey key in group)
                {
                    Roots.Add(NewKeyNode(key));
                }

                continue;
            }

            ThemeKeyNode groupNode = new(Strings.Get("ThemeGroup_" + group.Key.Replace(" ", string.Empty, StringComparison.Ordinal)), null);
            foreach (ThemeKey key in group)
            {
                groupNode.Children.Add(NewKeyNode(key));
            }

            Roots.Add(groupNode);
        }

        _selected = _keyNodes.FirstOrDefault();
        _owner.ThemeEdited += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Which page.</summary>
    public ThemePage Page { get; }

    /// <summary>The groups and the keys without a group, in the order of the registry.</summary>
    public ObservableCollection<ThemeKeyNode> Roots { get; } = new();

    /// <summary>The effects (without "none": the effect check box turns it off).</summary>
    public IReadOnlyList<EffectOption> Effects { get; }

    /// <summary>The selected node of the tree.</summary>
    [ObservableProperty]
    private ThemeKeyNode? _selected;

    partial void OnSelectedChanged(ThemeKeyNode? value) => RefreshPanel();

    /// <summary>The selected key; <c>null</c> when a group (or nothing) is selected.</summary>
    public ThemeKey? Key => Selected?.Key;

    /// <summary>Whether a key is selected (the panel is shown).</summary>
    public bool HasKey => Key is not null;

    /// <summary>Whether the selected key is a single color.</summary>
    public bool IsColorKey => Key?.Kind == ThemeKeyKind.Color;

    /// <summary>Whether the selected key is the look of text.</summary>
    public bool IsAttributesKey => Key?.Kind == ThemeKeyKind.TextAttributes;

    // ---- Inheritance ----

    /// <summary>Whether the selected key has a base it can take its values from.</summary>
    public bool CanInherit => Key?.BaseId is not null;

    /// <summary>The name of the base of the selected key (the link next to "Inherit values from").</summary>
    public string BaseName => Key?.BaseId is { } baseId ? KeyName(ThemeKeys.Get(baseId)) : string.Empty;

    /// <summary>
    /// "Inherit values from …": the key takes the values of its base. Turned off, the key keeps the values it showed,
    /// as its own (IntelliJ does the same).
    /// </summary>
    public bool Inherit
    {
        get => Key is { BaseId: not null } key && _owner.Resolved is { } theme && IsInherited(theme.Theme, key);
        set
        {
            if (Key is not { BaseId: not null } key || value == Inherit)
            {
                return;
            }

            _owner.SetKey(key, value ? ThemeEntry.Inherited : ThemeEntry.Of(Attributes));
        }
    }

    /// <summary>Whether the values of the key can be changed (a key is selected and it does not inherit them).</summary>
    public bool IsEditable => HasKey && !Inherit;

    /// <summary>Shows the base of the selected key (the link next to "Inherit values from").</summary>
    [RelayCommand]
    private void ShowBase()
    {
        if (Key?.BaseId is { } baseId)
        {
            _owner.RequestKey(ThemeKeys.Get(baseId));
        }
    }

    // ---- Text attributes ----

    /// <summary>Bold text.</summary>
    public bool Bold
    {
        get => Attributes.Bold;
        set => SetAttributes(Attributes with { Bold = value });
    }

    /// <summary>Italic text.</summary>
    public bool Italic
    {
        get => Attributes.Italic;
        set => SetAttributes(Attributes with { Italic = value });
    }

    /// <summary>Whether the key sets the text color.</summary>
    public bool HasForeground
    {
        get => Attributes.Foreground is not null;
        set => SetAttributes(Attributes with { Foreground = value ? Attributes.Foreground ?? TextColor : null });
    }

    /// <summary>The text color (<c>#RRGGBB</c> / <c>#AARRGGBB</c>); a text that is not a color is ignored.</summary>
    public string Foreground
    {
        get => Attributes.Foreground?.ToString() ?? string.Empty;
        set
        {
            if (ThemeColor.TryParse(value, out ThemeColor color))
            {
                SetAttributes(Attributes with { Foreground = color });
            }
        }
    }

    /// <summary>Whether the key sets the background.</summary>
    public bool HasBackground
    {
        get => Attributes.Background is not null;
        set => SetAttributes(Attributes with { Background = value ? Attributes.Background ?? BackgroundColor : null });
    }

    /// <summary>The background (<c>#RRGGBB</c> / <c>#AARRGGBB</c>); a text that is not a color is ignored.</summary>
    public string Background
    {
        get => Attributes.Background?.ToString() ?? string.Empty;
        set
        {
            if (ThemeColor.TryParse(value, out ThemeColor color))
            {
                SetAttributes(Attributes with { Background = color });
            }
        }
    }

    /// <summary>Whether the key has an effect (a line under or through the text, a frame).</summary>
    public bool HasEffect
    {
        get => Attributes.Effect != EffectType.None;
        set => SetAttributes(Attributes with { Effect = value ? EffectType.Underline : EffectType.None, EffectColor = value ? Attributes.EffectColor : null });
    }

    /// <summary>The effect.</summary>
    public EffectOption? Effect
    {
        get => Effects.FirstOrDefault(e => e.Value == Attributes.Effect);
        set
        {
            if (value is not null)
            {
                SetAttributes(Attributes with { Effect = value.Value });
            }
        }
    }

    /// <summary>Whether the effect has a color of its own (otherwise it takes the color of the text).</summary>
    public bool HasEffectColor
    {
        get => Attributes.EffectColor is not null;
        set => SetAttributes(Attributes with { EffectColor = value ? Attributes.EffectColor ?? Attributes.Foreground ?? TextColor : null });
    }

    /// <summary>The color of the effect (<c>#RRGGBB</c> / <c>#AARRGGBB</c>); a text that is not a color is ignored.</summary>
    public string EffectColor
    {
        get => Attributes.EffectColor?.ToString() ?? string.Empty;
        set
        {
            if (ThemeColor.TryParse(value, out ThemeColor color))
            {
                SetAttributes(Attributes with { EffectColor = color });
            }
        }
    }

    // ---- Color ----

    /// <summary>
    /// Whether the selected color key may have no color (e.g. the accent: the system's; the background of the book in
    /// Preview: the book's) — the "own color" check box is shown then.
    /// </summary>
    public bool CanBeEmpty => Key is { Kind: ThemeKeyKind.Color, DefaultColor: null };

    /// <summary>Whether the color key has a color of its own (always for a key that cannot be empty).</summary>
    public bool HasColor
    {
        get => ColorValue is not null;
        set
        {
            if (Key is { Kind: ThemeKeyKind.Color } key && value != HasColor)
            {
                _owner.SetKey(key, ThemeEntry.Of(value ? ColorValue ?? TextColor : null));
            }
        }
    }

    /// <summary>The color of the color key (<c>#RRGGBB</c> / <c>#AARRGGBB</c>); a text that is not a color is ignored.</summary>
    public string Color
    {
        get => ColorValue?.ToString() ?? string.Empty;
        set
        {
            if (Key is { Kind: ThemeKeyKind.Color } key && ThemeColor.TryParse(value, out ThemeColor color))
            {
                _owner.SetKey(key, ThemeEntry.Of(color));
            }
        }
    }

    // ---- Changed keys ----

    /// <summary>Whether the theme changes the selected key (the restore link is shown).</summary>
    public bool IsModified => Key is { } key && _owner.ChangesKey(key);

    /// <summary>The restore link: "Restore the value of ‹parent›", or "Restore the default value" in a bundled theme.</summary>
    public string RestoreText => _owner.RestoreSource is { } parent
        ? Strings.Format("ColorScheme_RestoreFromParent", parent)
        : Strings.Get("ColorScheme_RestoreDefault");

    /// <summary>Undoes the change of the selected key.</summary>
    [RelayCommand]
    private void Restore()
    {
        if (Key is { } key)
        {
            _owner.RestoreKey(key);
        }
    }

    // ---- Picking colors ----

    /// <summary>Picks the text color.</summary>
    [RelayCommand]
    private async Task PickForeground()
    {
        if (await Pick("ColorScheme_Foreground", Attributes.Foreground ?? TextColor) is { } color)
        {
            SetAttributes(Attributes with { Foreground = color });
        }
    }

    /// <summary>Picks the background.</summary>
    [RelayCommand]
    private async Task PickBackground()
    {
        if (await Pick("ColorScheme_Background", Attributes.Background ?? BackgroundColor) is { } color)
        {
            SetAttributes(Attributes with { Background = color });
        }
    }

    /// <summary>Picks the color of the effect.</summary>
    [RelayCommand]
    private async Task PickEffectColor()
    {
        if (await Pick("ColorScheme_EffectColor", Attributes.EffectColor ?? Attributes.Foreground ?? TextColor) is { } color)
        {
            SetAttributes(Attributes with { EffectColor = color });
        }
    }

    /// <summary>Picks the color of a color key.</summary>
    [RelayCommand]
    private async Task PickColor()
    {
        if (Key is { Kind: ThemeKeyKind.Color } key && await Pick("ColorScheme_Color", ColorValue ?? TextColor) is { } color)
        {
            _owner.SetKey(key, ThemeEntry.Of(color));
        }
    }

    /// <summary>Selects the key in the tree (expanding its group).</summary>
    public void SelectKey(ThemeKey key)
    {
        ThemeKeyNode? node = _keyNodes.FirstOrDefault(n => n.Key == key);
        if (node is null)
        {
            return;
        }

        foreach (ThemeKeyNode group in Roots.Where(r => r.Children.Contains(node)))
        {
            group.IsExpanded = true;
        }

        Selected = node;
    }

    /// <summary>The name of a key in the tree: its own resource, or the name of the Fluent palette color.</summary>
    internal static string KeyName(ThemeKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Group == "Palette")
        {
            return ThemeKeys.FluentPalette.First(p => p.Key == key).PaletteColor;
        }

        return Strings.Get("ThemeKey_" + key.Id.Replace('.', '_'));
    }

    // The resolved look of the selected attributes key (the base's when it inherits); empty for a color key.
    private TextAttributes Attributes =>
        Key is { Kind: ThemeKeyKind.TextAttributes } key && _owner.Resolved is { } theme ? theme.Attributes(key) : TextAttributes.Empty;

    // The resolved color of the selected color key.
    private ThemeColor? ColorValue =>
        Key is { Kind: ThemeKeyKind.Color } key && _owner.Resolved is { } theme ? theme.Resolver.Color(theme.Theme, key) : null;

    // The colors of the editor: the start of a text color or a background turned on.
    private ThemeColor TextColor => EditorText.Foreground ?? ThemeColor.FromRgb(0, 0, 0);

    private ThemeColor BackgroundColor => EditorText.Background ?? ThemeColor.FromRgb(0xFF, 0xFF, 0xFF);

    private TextAttributes EditorText => _owner.Resolved?.Attributes(ThemeKeys.EditorText) ?? TextAttributes.Empty;

    // Whether the key inherits in the theme: the nearest entry of the lineage says so, or no theme of the lineage sets
    // the key (it then takes its base, see ThemeResolver).
    private bool IsInherited(Theme theme, ThemeKey key) =>
        _owner.Resolved?.Resolver.Owner(theme, key) is not { } owner || owner.Entries[key.Id].Inherit;

    private void SetAttributes(TextAttributes attributes)
    {
        if (Key is { Kind: ThemeKeyKind.TextAttributes } key && IsEditable && attributes != Attributes)
        {
            _owner.SetKey(key, ThemeEntry.Of(attributes));
        }
    }

    private async Task<ThemeColor?> Pick(string titleKey, ThemeColor initial) =>
        _owner.ColorPicker is { } picker && await picker(Strings.Get(titleKey), initial.ToString()) is { } text
        && ThemeColor.TryParse(text, out ThemeColor color)
            ? color
            : null;

    private ThemeKeyNode NewKeyNode(ThemeKey key)
    {
        ThemeKeyNode node = new(KeyName(key), key);
        _keyNodes.Add(node);
        return node;
    }

    // The theme or a key changed: the markers of the tree and the panel.
    private void Refresh()
    {
        foreach (ThemeKeyNode node in _keyNodes)
        {
            node.IsModified = _owner.ChangesKey(node.Key!);
        }

        foreach (ThemeKeyNode group in Roots.Where(r => r.IsGroup))
        {
            group.IsModified = group.Children.Any(c => c.IsModified);
        }

        RefreshPanel();
    }

    private void RefreshPanel()
    {
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(HasKey));
        OnPropertyChanged(nameof(IsColorKey));
        OnPropertyChanged(nameof(IsAttributesKey));
        OnPropertyChanged(nameof(CanInherit));
        OnPropertyChanged(nameof(BaseName));
        OnPropertyChanged(nameof(Inherit));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(Bold));
        OnPropertyChanged(nameof(Italic));
        OnPropertyChanged(nameof(HasForeground));
        OnPropertyChanged(nameof(Foreground));
        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(HasEffect));
        OnPropertyChanged(nameof(Effect));
        OnPropertyChanged(nameof(HasEffectColor));
        OnPropertyChanged(nameof(EffectColor));
        OnPropertyChanged(nameof(CanBeEmpty));
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(RestoreText));
    }
}
