using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// How a font setting inherits from a parent setting (e.g. the tooltip font of an area from the one of the other windows):
/// while <see cref="Get"/> is on, the setting's own typeface and size are not used and its controls are switched off.
/// </summary>
/// <param name="Get">Whether the setting inherits.</param>
/// <param name="Set">Turns the inheritance on or off.</param>
/// <param name="ParentPath">Where the parent is in the Preferences window (e.g. <c>Appearance › General › Fonts › …</c>).</param>
/// <param name="ParentFont">Describes the font inherited now (typeface and size).</param>
/// <param name="Parent">The parent setting (to go to).</param>
internal sealed record FontInheritance(
    Func<bool> Get, Action<bool> Set, string ParentPath, Func<string> ParentFont, Func<FontSettingViewModel> Parent);

/// <summary>
/// One font setting on a "Fonts" page of the Preferences window (Rider's Editor › Font): the font as one line (typeface,
/// size, weight) and "Choose…" (the font picker window, the only way to change it) and a sample for the preview on the right. The setting itself
/// stays in <see cref="PreferencesViewModel"/>; this is an adapter over its getter and setter, refreshed with
/// <see cref="Refresh"/> when the setting changes elsewhere (e.g. "Restore defaults").
/// </summary>
public sealed class FontSettingViewModel : ObservableObject
{
    private readonly Func<string> _getFamily;
    private readonly Action<string>? _setFamily;
    private readonly Func<int> _getSize;
    private readonly Action<int>? _setSize;
    private readonly Func<string> _effectiveFamily;
    private readonly Func<double> _effectiveSize;
    private readonly Func<string> _placeholder;
    private readonly Func<string?> _previewForeground;
    private readonly Func<string?> _previewBackground;
    private readonly Func<bool>? _codePreview;
    private readonly Action _restoreDefault;
    private readonly FontInheritance? _inheritance;
    private readonly Func<int> _getWeight;
    private readonly Action<int>? _setWeight;
    private readonly Func<int> _effectiveWeight;

    /// <summary>Creates the adapter of one font setting.</summary>
    /// <param name="key">An English identifier (tests, the view's control names), e.g. <c>"CodeEditorText"</c>.</param>
    /// <param name="label">The label in the UI language.</param>
    /// <param name="previewText">The sample shown in the preview, fitting the place the font is used in.</param>
    /// <param name="family">The stored typeface and how to change it; <c>null</c> for a size-only setting.</param>
    /// <param name="size">The stored size and how to change it; <c>null</c> for a typeface-only setting.</param>
    /// <param name="effective">The typeface and size actually used (the defaults resolved) — for the preview.</param>
    /// <param name="restoreDefault">Sets the default typeface and size ("Restore defaults" of the page).</param>
    /// <param name="choose">"Choose…" — the font picker window; <c>null</c> for a size-only setting.</param>
    /// <param name="placeholder">Describes the default typeface, shown while the stored one is empty; without it the list
    /// shows the stored name when that font is not installed.</param>
    /// <param name="previewForeground">The text color of the preview, when the place has its own.</param>
    /// <param name="previewBackground">The background of the preview, when the place has its own.</param>
    /// <param name="sizeHint">What the size field means besides a size, e.g. "0 = as the interface text".</param>
    /// <param name="codePreview">For a font of the code editor: whether the preview highlights the sample with the
    /// extended highlighting (it shows it as Code Editor does, in the colors of the theme, with line numbers); <c>null</c>
    /// = the sample is plain text.</param>
    /// <param name="inheritance">Lets the setting use the font of a parent setting instead of its own; <c>null</c> = it
    /// has no parent.</param>
    /// <param name="weight">The stored weight (<see cref="Signet.Core.Misc.FontWeightScale"/>, 0 = not set) and how to change
    /// it; <c>null</c> = the setting has no weight (no list of weights).</param>
    /// <param name="effectiveWeight">The weight actually used — for the preview; by default the stored one.</param>
    internal FontSettingViewModel(
        string key,
        string label,
        string previewText,
        (Func<string> Get, Action<string> Set)? family,
        (Func<int> Get, Action<int> Set, int Min, int Max)? size,
        (Func<string> Family, Func<double> Size) effective,
        Action restoreDefault,
        ICommand? choose,
        Func<string>? placeholder = null,
        Func<string?>? previewForeground = null,
        Func<string?>? previewBackground = null,
        string? sizeHint = null,
        Func<bool>? codePreview = null,
        FontInheritance? inheritance = null,
        (Func<int> Get, Action<int> Set)? weight = null,
        Func<int>? effectiveWeight = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        PreviewText = previewText ?? throw new ArgumentNullException(nameof(previewText));
        _getFamily = family?.Get ?? (() => string.Empty);
        _setFamily = family?.Set;
        _getSize = size?.Get ?? (() => 0);
        _setSize = size?.Set;
        MinSize = size?.Min ?? 0;
        MaxSize = size?.Max ?? 0;
        _effectiveFamily = effective.Family;
        _effectiveSize = effective.Size;
        _restoreDefault = restoreDefault ?? throw new ArgumentNullException(nameof(restoreDefault));
        ChooseCommand = choose;
        // Without a default to describe, the list shows the stored name while the font is not installed here.
        _placeholder = placeholder ?? (() => _getFamily());
        _previewForeground = previewForeground ?? (() => null);
        _previewBackground = previewBackground ?? (() => null);
        SizeHint = sizeHint;
        _codePreview = codePreview;
        _inheritance = inheritance;
        _getWeight = weight?.Get ?? (() => 0);
        _setWeight = weight?.Set;
        _effectiveWeight = effectiveWeight ?? (() => _getWeight());
    }

    /// <summary>Raised when the user changed the typeface or the size here.</summary>
    internal event EventHandler? Edited;

    /// <summary>An English identifier of the setting.</summary>
    public string Key { get; }

    /// <summary>The group the row is shown under (a heading above the first row of a group); <c>null</c> = none.</summary>
    public string? Group { get; init; }

    /// <summary>The label in the UI language.</summary>
    public string Label { get; }

    /// <summary>The sample shown in the preview.</summary>
    public string PreviewText { get; }

    /// <summary>Whether the setting has a typeface (otherwise only a size).</summary>
    public bool HasFamily => _setFamily is not null;

    /// <summary>Whether the setting has a size (otherwise only a typeface).</summary>
    public bool HasSize => _setSize is not null;

    /// <summary>Whether the setting has a weight (Light, Regular, SemiBold…).</summary>
    public bool HasWeight => _setWeight is not null;

    /// <summary>The smallest size the size field accepts.</summary>
    public int MinSize { get; }

    /// <summary>The largest size the size field accepts.</summary>
    public int MaxSize { get; }

    /// <summary>What the size field means besides a size (e.g. "0 = as the interface text"); <c>null</c> = nothing.</summary>
    public string? SizeHint { get; }

    /// <summary>"Choose…" — the font picker window; <c>null</c> for a size-only setting.</summary>
    public ICommand? ChooseCommand { get; }

    /// <summary>Whether the setting can use the font of a parent setting.</summary>
    public bool HasInheritance => _inheritance is not null;

    /// <summary>Whether the setting uses the font of its parent (the typeface and size controls are then switched off).</summary>
    public bool InheritFromParent
    {
        get => _inheritance?.Get() ?? false;
        set
        {
            if (_inheritance is not null && value != _inheritance.Get())
            {
                _inheritance.Set(value);
                Changed();
            }
        }
    }

    /// <summary>Whether the typeface and size of the setting are its own (they can be changed).</summary>
    public bool IsOwnFont => !InheritFromParent;

    /// <summary>Where the parent setting is in the Preferences window; empty without a parent.</summary>
    public string ParentPath => _inheritance?.ParentPath ?? string.Empty;

    /// <summary>The typeface and size inherited from the parent now; empty without a parent.</summary>
    public string ParentFont => _inheritance?.ParentFont() ?? string.Empty;

    /// <summary>The parent setting; <c>null</c> without one.</summary>
    public FontSettingViewModel? Parent => _inheritance?.Parent();

    /// <summary>The stored typeface; empty = the default, described by <see cref="Placeholder"/>.</summary>
    public string Family
    {
        get => _getFamily();
        set
        {
            if (_setFamily is not null && value is not null && value != _getFamily())
            {
                _setFamily(value);
                Changed();
            }
        }
    }

    /// <summary>The stored size (px).</summary>
    public int Size
    {
        get => _getSize();
        set
        {
            if (_setSize is not null && value != _getSize())
            {
                _setSize(value);
                Changed();
            }
        }
    }

    /// <summary>The stored weight (<see cref="Signet.Core.Misc.FontWeightScale"/>); 0 = not set.</summary>
    public int Weight
    {
        get => _getWeight();
        set
        {
            if (_setWeight is not null && value != _getWeight())
            {
                _setWeight(value);
                Changed();
            }
        }
    }

    /// <summary>
    /// The font as the row shows it, in one line: "Segoe UI, 16, Light" — the typeface, the size and the weight, each only
    /// when the setting has it and it is set; "(default)" stands for a typeface that is not set.
    /// </summary>
    public string Summary
    {
        get
        {
            List<string> parts = new();
            if (HasFamily)
            {
                parts.Add(Family.Length > 0 ? Family : Strings.Get("FontWeight_Default"));
            }

            if (HasSize && Size > 0)
            {
                parts.Add(Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (HasWeight && FontWeightScale.NameOf(Weight) is { } weight)
            {
                parts.Add(weight);
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>Whether the setting is only a size (no typeface to choose): it has a size field instead of "Choose…".</summary>
    public bool IsSizeOnly => HasSize && !HasFamily;

    /// <summary>The weight actually used — for the preview (0 = Regular).</summary>
    public int EffectiveWeight => _effectiveWeight();

    /// <summary>What the typeface list shows while nothing in it is selected (the default, or a font not installed).</summary>
    public string Placeholder => _placeholder();

    /// <summary>The typeface actually used — for the preview.</summary>
    public string EffectiveFamily => _effectiveFamily();

    /// <summary>The size actually used (px) — for the preview.</summary>
    public double EffectiveSize => _effectiveSize();

    /// <summary>The text color of the preview (<c>#rrggbb</c>), when the place the font is used in has its own.</summary>
    public string? PreviewForeground => _previewForeground();

    /// <summary>The background of the preview (<c>#rrggbb</c>), when the place the font is used in has its own.</summary>
    public string? PreviewBackground => _previewBackground();

    /// <summary>Whether the preview shows the sample as code: highlighted as in Code Editor, with line numbers.</summary>
    public bool IsCodePreview => _codePreview is not null;

    /// <summary>Whether the code preview uses the extended highlighting of Code Editor.</summary>
    public bool CodePreviewExtended => _codePreview?.Invoke() ?? false;

    /// <summary>Sets the default typeface and size.</summary>
    public void RestoreDefault()
    {
        _restoreDefault();
        Refresh();
    }

    /// <summary>Announces that the values may have changed (the setting was changed elsewhere).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void Changed()
    {
        Refresh();
        Edited?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// A "Fonts" page of the Preferences window: its font settings, the one shown in the preview (the one focused or
/// changed last, as in Rider) and "Restore defaults" of the page.
/// </summary>
public sealed partial class FontPageViewModel : ObservableObject
{
    /// <summary>Creates the page.</summary>
    /// <param name="fonts">The font settings of the page, in their order.</param>
    /// <param name="note">A note under the settings (e.g. that a restart is needed); <c>null</c> = none.</param>
    internal FontPageViewModel(IEnumerable<FontSettingViewModel> fonts, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = new ReadOnlyObservableCollection<FontSettingViewModel>(new ObservableCollection<FontSettingViewModel>(fonts));
        Note = note;
        foreach (FontSettingViewModel font in Fonts)
        {
            font.Edited += (_, _) => Active = font;
        }

        _active = Fonts.Count > 0 ? Fonts[0] : null;
    }

    /// <summary>The font settings of the page.</summary>
    public ReadOnlyObservableCollection<FontSettingViewModel> Fonts { get; }

    /// <summary>A note under the settings; <c>null</c> = none.</summary>
    public string? Note { get; }

    /// <summary>Whether there is a note.</summary>
    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>The setting shown in the preview: the one focused or changed last (the first one at the start).</summary>
    [ObservableProperty]
    private FontSettingViewModel? _active;

    /// <summary>Whether the preview wraps the lines that do not fit its width (it scrolls sideways otherwise).</summary>
    [ObservableProperty]
    private bool _wrapPreview;

    /// <summary>"Restore defaults" — every font of the page goes back to its default.</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        foreach (FontSettingViewModel font in Fonts)
        {
            font.RestoreDefault();
        }
    }

    /// <summary>Announces that the values of every setting may have changed.</summary>
    public void Refresh()
    {
        foreach (FontSettingViewModel font in Fonts)
        {
            font.Refresh();
        }
    }
}
