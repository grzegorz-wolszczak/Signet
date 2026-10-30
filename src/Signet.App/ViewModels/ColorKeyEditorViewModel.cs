using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
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
    }
}
