using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>The color of a text attributes key that a <see cref="ColorKeyEditorViewModel"/> edits.</summary>
public enum TextAttributesColor
{
    /// <summary>The text color.</summary>
    Foreground,

    /// <summary>The background.</summary>
    Background,
}

/// <summary>
/// A compact editor of one Color-kind theme key (or of one color of a text attributes key) — the "own color" check box, the hex field and the swatch picker,
/// the same controls as <see cref="ThemeKeysPageViewModel"/>'s single-color panel, but over a key fixed at
/// construction instead of the tree's selection. Embedded directly in a panel's own Preferences section (e.g.
/// Appearance › Notifications), next to the panel's other settings, instead of sending the user to the shared
/// Interface Colors tree for a color only that panel uses.
/// </summary>
public sealed partial class ColorKeyEditorViewModel : ObservableObject
{
    private readonly ThemeDocumentViewModel _owner;
    private readonly ThemeKey _key;
    private readonly TextAttributesColor? _part;

    /// <summary>Creates the editor of <paramref name="key"/> over <paramref name="owner"/> (the same document the
    /// shared Interface Colors / Color Scheme pages edit — the changes are the same draft, until the same Save).</summary>
    internal ColorKeyEditorViewModel(ThemeDocumentViewModel owner, ThemeKey key)
        : this(owner, key, null)
    {
    }

    /// <summary>Creates the editor of one color (<paramref name="part"/>) of the text attributes key
    /// <paramref name="key"/>; the other attributes of the key stay as they are.</summary>
    internal ColorKeyEditorViewModel(ThemeDocumentViewModel owner, ThemeKey key, TextAttributesColor? part)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(key);
        ThemeKeyKind expected = part is null ? ThemeKeyKind.Color : ThemeKeyKind.TextAttributes;
        if (key.Kind != expected)
        {
            throw new ArgumentException($"The theme key '{key.Id}' is not a {expected} key.", nameof(key));
        }

        _key = key;
        _part = part;
        _owner.ThemeEdited += (_, _) => Refresh();
    }

    /// <summary>The key this editor edits — the window finds the page that holds a key by it.</summary>
    internal ThemeKey Key => _key;

    /// <summary>Whether the key may have no color of its own — the "own color" check box is shown then. A color of
    /// text attributes can always be left unset.</summary>
    public bool CanBeEmpty => _part is not null || _key.DefaultColor is null;

    /// <summary>Whether the key has a color of its own (always for a key that cannot be empty).</summary>
    public bool HasColor
    {
        get => ColorValue is not null;
        set
        {
            if (value != HasColor)
            {
                Set(value ? ColorValue ?? TextColor : null);
            }
        }
    }

    /// <summary>The color (<c>#RRGGBB</c> / <c>#AARRGGBB</c>); a text that is not a color is ignored.</summary>
    public string Color
    {
        get => ColorValue?.ToString() ?? string.Empty;
        set
        {
            if (ThemeColor.TryParse(value, out ThemeColor color))
            {
                Set(color);
            }
        }
    }

    /// <summary>
    /// Whether the key has no color of its own and takes one from somewhere else — the hex field then shows that
    /// color instead of nothing, and the swatch is drawn in it (the button stays switched off).
    /// </summary>
    public bool IsInherited => !HasColor;

    // The key the color comes from while this one is unset; null = nothing names it (the platform decides) or this is
    // one color of a text attributes key, which has no fallback chain of its own.
    private ThemeKey? Source => _part is null && !HasColor ? _owner.Resolved?.ColorSource(_key) : null;

    /// <summary>Whether the inherited color is known — it comes from another setting, which the editor can name.</summary>
    public bool HasInheritedColor => Source is not null || SystemAccentValue is not null;

    /// <summary>Whether the inherited color links to another setting (the accent of the system links nowhere).</summary>
    public bool HasInheritedSource => Source is not null;

    /// <summary>
    /// Whether the color comes from the theme of the system and not from another setting: there is no value to show
    /// and nowhere to go, so the field says so instead of a hex.
    /// </summary>
    public bool IsFromPlatform =>
        IsInherited && Source is null && SystemAccentValue is null && _part != TextAttributesColor.Background;

    /// <summary>Whether the value shown is the accent of the system (no setting of Signet holds it).</summary>
    public bool IsSystemAccent => SystemAccentValue is not null;

    /// <summary>
    /// Whether this is a background that is not set at all: nothing is painted and what is behind shows through,
    /// which is not the same as a color taken from the theme.
    /// </summary>
    public bool IsNoBackground => IsInherited && Source is null && _part == TextAttributesColor.Background;

    /// <summary>The inherited color as a hex (empty when it is not known).</summary>
    public string InheritedColor =>
        Source is { } source ? _owner.Resolved?.ColorOf(source)?.ToString() ?? string.Empty
            : SystemAccentValue?.ToString() ?? string.Empty;

    // The colors the system gives and Signet writes down nowhere (the accent, the shade of it a context menu title
    // takes): no setting holds them, but they still have a value to show.
    private ThemeColor? SystemAccentValue => _part is null && !HasColor ? AppTheme.SystemColorOf(_key) : null;

    /// <summary>"Inherited from ‹setting›" — the tip of the link that goes to the setting the color comes from.</summary>
    public string InheritedFromText =>
        Source is { } source ? Strings.Format("ColorScheme_InheritedFrom", ThemeKeysPageViewModel.KeyName(source)) : string.Empty;

    /// <summary>The color the field is filled with: the key's own, or the one it inherits.</summary>
    public string SwatchColor => HasColor ? Color : InheritedColor;

    /// <summary>Whether the field has a color to be filled with (its own or an inherited one).</summary>
    public bool HasFieldColor => SwatchColor.Length > 0;

    /// <summary>
    /// What the field says: the hex of the color, or — when there is none — what the color falls back to instead.
    /// </summary>
    public string FieldText => SwatchColor.Length > 0
        ? SwatchColor
        : Strings.Get(IsNoBackground ? "ColorScheme_NoBackground" : "ColorScheme_FromPlatform");

    /// <summary>
    /// Black or white, whichever is readable on <see cref="SwatchColor"/> — the hex is written on the color itself,
    /// so the text has to follow it. Empty when there is no color (the field keeps the usual text color then).
    /// </summary>
    public string FieldTextColor =>
        ThemeColor.TryParse(SwatchColor, out ThemeColor color)
            ? ColorContrast.ReadableOn(color, IsDarkSurface).ToString()
            : string.Empty;

    // A color that is not opaque is seen over the surface of the window, so the text follows what is seen.
    private bool IsDarkSurface => _owner.Resolved?.Variant == Avalonia.Styling.ThemeVariant.Dark;

    /// <summary>Goes to the setting the inherited color comes from.</summary>
    [RelayCommand]
    private void GoToSource()
    {
        if (Source is { } source)
        {
            _owner.RequestKey(source);
        }
    }

    /// <summary>Picks the color.</summary>
    [RelayCommand]
    private async Task PickColor()
    {
        if (_owner.ColorPicker is { } picker
            && await picker(Strings.Get("ColorScheme_Color"), (ColorValue ?? TextColor).ToString()) is { } text
            && ThemeColor.TryParse(text, out ThemeColor color))
        {
            Set(color);
        }
    }

    // The resolved color of the key (with the changes not saved yet).
    private ThemeColor? ColorValue => _part switch
    {
        null => _owner.Resolved?.ColorOf(_key),
        TextAttributesColor.Foreground => Attributes.Foreground,
        _ => Attributes.Background,
    };

    // The resolved attributes of a text attributes key.
    private TextAttributes Attributes => _owner.Resolved?.Attributes(_key) ?? TextAttributes.Empty;

    private void Set(ThemeColor? color)
    {
        ThemeEntry entry = _part switch
        {
            null => ThemeEntry.Of(color),
            TextAttributesColor.Foreground => ThemeEntry.Of(Attributes with { Foreground = color }),
            _ => ThemeEntry.Of(Attributes with { Background = color }),
        };
        _owner.SetKey(_key, entry);
    }

    // The start color offered when "own color" is turned on: the editor's text color.
    private ThemeColor TextColor => _owner.Resolved?.Attributes(ThemeKeys.EditorText).Foreground ?? ThemeColor.FromRgb(0, 0, 0);

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(IsInherited));
        OnPropertyChanged(nameof(HasInheritedColor));
        OnPropertyChanged(nameof(IsFromPlatform));
        OnPropertyChanged(nameof(IsNoBackground));
        OnPropertyChanged(nameof(IsSystemAccent));
        OnPropertyChanged(nameof(HasInheritedSource));
        OnPropertyChanged(nameof(InheritedColor));
        OnPropertyChanged(nameof(InheritedFromText));
        OnPropertyChanged(nameof(SwatchColor));
        OnPropertyChanged(nameof(HasFieldColor));
        OnPropertyChanged(nameof(FieldText));
        OnPropertyChanged(nameof(FieldTextColor));
    }
}
