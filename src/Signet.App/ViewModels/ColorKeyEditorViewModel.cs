using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>
/// A compact editor of one Color-kind theme key — the "own color" check box, the hex field and the swatch picker,
/// the same controls as <see cref="ThemeKeysPageViewModel"/>'s single-color panel, but over a key fixed at
/// construction instead of the tree's selection. Embedded directly in a panel's own Preferences section (e.g.
/// Appearance › Notifications), next to the panel's other settings, instead of sending the user to the shared
/// Interface Colors tree for a color only that panel uses.
/// </summary>
public sealed partial class ColorKeyEditorViewModel : ObservableObject
{
    private readonly ThemeDocumentViewModel _owner;
    private readonly ThemeKey _key;

    /// <summary>Creates the editor of <paramref name="key"/> over <paramref name="owner"/> (the same document the
    /// shared Interface Colors / Color Scheme pages edit — the changes are the same draft, until the same Save).</summary>
    internal ColorKeyEditorViewModel(ThemeDocumentViewModel owner, ThemeKey key)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(key);
        if (key.Kind != ThemeKeyKind.Color)
        {
            throw new ArgumentException($"The theme key '{key.Id}' is not a color key.", nameof(key));
        }

        _key = key;
        _owner.ThemeEdited += (_, _) => Refresh();
    }

    /// <summary>Whether the key may have no color of its own — the "own color" check box is shown then.</summary>
    public bool CanBeEmpty => _key.DefaultColor is null;

    /// <summary>Whether the key has a color of its own (always for a key that cannot be empty).</summary>
    public bool HasColor
    {
        get => ColorValue is not null;
        set
        {
            if (value != HasColor)
            {
                _owner.SetKey(_key, ThemeEntry.Of(value ? ColorValue ?? TextColor : null));
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
                _owner.SetKey(_key, ThemeEntry.Of(color));
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
            _owner.SetKey(_key, ThemeEntry.Of(color));
        }
    }

    // The resolved color of the key (with the changes not saved yet).
    private ThemeColor? ColorValue => _owner.Resolved?.ColorOf(_key);

    // The start color offered when "own color" is turned on: the editor's text color.
    private ThemeColor TextColor => _owner.Resolved?.Attributes(ThemeKeys.EditorText).Foreground ?? ThemeColor.FromRgb(0, 0, 0);

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(Color));
    }
}
