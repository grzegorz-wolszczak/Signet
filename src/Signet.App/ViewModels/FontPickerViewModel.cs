using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Infrastructure;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>A request to pick a font from Preferences.</summary>
/// <param name="Family">Current family (may be a fallback list "A, B" — the first one counts).</param>
/// <param name="Size">Current size, or <see langword="null"/> when the window should pick only the typeface.</param>
/// <param name="MonospaceOnly">Whether the "fixed-width only" filter should start enabled.</param>
/// <param name="Weight">
/// Current weight (<see cref="FontWeightScale"/>, 0 = not set), or <see langword="null"/> when the font being picked has no
/// weight — the window then has no list of weights.
/// </param>
/// <param name="Changed">
/// Called with the font chosen so far each time the user changes it in the window (before OK), so the caller can show the
/// effect live; the caller restores its previous font when the window is cancelled. <see langword="null"/> = no live effect.
/// </param>
public sealed record FontPickRequest(string Family, int? Size, bool MonospaceOnly, int? Weight = null, Action<FontPickResult>? Changed = null);

/// <summary>The result of the font picker window.</summary>
/// <param name="Family">Selected family.</param>
/// <param name="Size">Selected size (irrelevant when the request did not include a size).</param>
/// <param name="Weight">Selected weight (<see cref="FontWeightScale"/>, 0 = not set; irrelevant when the request had none).</param>
public sealed record FontPickResult(string Family, int Size, int Weight = 0);

/// <summary>
/// View model of the font picker window: the list of system fonts filtered by name and (optionally)
/// to fixed-width fonts, the selected font, size and preview.
/// </summary>
public sealed partial class FontPickerViewModel : ObservableObject
{
    /// <summary>Smallest selectable size.</summary>
    public const int MinSize = 6;

    /// <summary>Largest selectable size.</summary>
    public const int MaxSize = 72;

    private readonly IReadOnlyList<FontEntry> _fonts;
    private readonly Func<string, IReadOnlyList<int>> _weightsOf;

    /// <summary>Creates the model over the given font list (in the application: <see cref="FontCatalog.SystemFonts"/>).</summary>
    /// <param name="fonts">The fonts to choose from.</param>
    /// <param name="request">What is picked and where the choice starts.</param>
    /// <param name="weightsOf">The weights a family has; by default what the font manager finds (<see cref="FontCatalog.WeightsOf"/>).</param>
    public FontPickerViewModel(IReadOnlyList<FontEntry> fonts, FontPickRequest request, Func<string, IReadOnlyList<int>>? weightsOf = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(request);
        _fonts = fonts;
        _weightsOf = weightsOf ?? FontCatalog.WeightsOf;

        ShowSize = request.Size is not null;
        ShowWeight = request.Weight is not null;
        _requestedWeight = FontWeightScale.Normalize(request.Weight ?? 0);
        _size = Math.Clamp(request.Size ?? 12, MinSize, MaxSize);

        string current = request.Family.Split(',')[0].Trim().Trim('"', '\'');
        FontEntry? initial = fonts.FirstOrDefault(f => string.Equals(f.Name, current, StringComparison.OrdinalIgnoreCase));

        // The current font must be visible and selected — if the monospace filter would hide it, it starts disabled.
        _monospaceOnly = request.MonospaceOnly && (initial is null || initial.IsMonospace);
        ApplyFilter();
        SelectedFont = initial is not null && VisibleFonts.Contains(initial) ? initial : null;
        _initialFont = SelectedFont;
        RebuildWeights();

        // Only from now on a change is the user's.
        _changed = request.Changed;
    }

    // Tells the caller about the font chosen so far; not while the list of weights is rebuilt (it is announced once after).
    private readonly Action<FontPickResult>? _changed;
    private bool _rebuilding;

    private void NotifyChanged()
    {
        if (_changed is not null && !_rebuilding && Result() is { } result)
        {
            _changed(result);
        }
    }

    // The font the window started with: the weight stored with it stays selectable even when the font lacks it.
    private readonly FontEntry? _initialFont;

    // The weight chosen last: it stays selected while the font changes, as long as the new font has it.
    private int _requestedWeight;

    /// <summary>Fonts matching the filter.</summary>
    public ObservableCollection<FontEntry> VisibleFonts { get; } = new();

    /// <summary>Filter text (a fragment of the name, case-insensitive).</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>Whether to show only fixed-width fonts.</summary>
    [ObservableProperty]
    private bool _monospaceOnly;

    /// <summary>The selected font, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private FontEntry? _selectedFont;

    /// <summary>Whether the window has a size field.</summary>
    public bool ShowSize { get; }

    /// <summary>Whether the window has a list of weights (Light, Regular, SemiBold…).</summary>
    public bool ShowWeight { get; }

    /// <summary>The weights of the selected font: "(default)" first, then the ones the font has.</summary>
    public ObservableCollection<FontWeightOption> Weights { get; } = new();

    /// <summary>The selected weight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewWeight))]
    private FontWeightOption? _selectedWeight;

    /// <summary>The weight the preview shows the sample in.</summary>
    public Avalonia.Media.FontWeight PreviewWeight => FontWeightOptions.ToAvalonia(SelectedWeight?.Value ?? 0);

    /// <summary>Selected size (as <see cref="decimal"/> for <c>NumericUpDown</c>).</summary>
    [ObservableProperty]
    private decimal _size;

    /// <summary>Whether the choice can be accepted (a font is selected).</summary>
    public bool CanAccept => SelectedFont is not null;

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedFontChanged(FontEntry? value)
    {
        RebuildWeights();
        NotifyChanged();
    }

    partial void OnSelectedWeightChanged(FontWeightOption? value)
    {
        if (value is not null)
        {
            _requestedWeight = value.Value;
            NotifyChanged();
        }
    }

    partial void OnSizeChanged(decimal value) => NotifyChanged();

    partial void OnMonospaceOnlyChanged(bool value) => ApplyFilter();

    /// <summary>The result to return after OK, or <see langword="null"/> when nothing is selected.</summary>
    public FontPickResult? Result() =>
        SelectedFont is { } font
            ? new FontPickResult(font.Name, (int)Math.Clamp(Math.Round(Size), MinSize, MaxSize), SelectedWeight?.Value ?? 0)
            : null;

    // The list of weights follows the selected font; the weight wanted is kept when the font has it (otherwise "(default)").
    private void RebuildWeights()
    {
        if (!ShowWeight)
        {
            return;
        }

        int wanted = _requestedWeight;
        List<int> weights = _weightsOf(SelectedFont?.Name ?? string.Empty).ToList();

        // A weight stored with the font the window started with and missing in it stays selectable (shown by its name or
        // number), so it is not lost silently; a font chosen later offers only what it has.
        if (wanted > 0 && !weights.Contains(wanted) && Equals(SelectedFont, _initialFont))
        {
            weights.Add(wanted);
        }

        _rebuilding = true;
        try
        {
            Weights.Clear();
            Weights.Add(FontWeightOptions.Default);
            foreach (int weight in weights.Order())
            {
                Weights.Add(new FontWeightOption(weight, FontWeightScale.NameOf(weight)!));
            }

            SelectedWeight = Weights.FirstOrDefault(w => w.Value == wanted) ?? Weights[0];
            _requestedWeight = wanted;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void ApplyFilter()
    {
        FontEntry? keep = SelectedFont;
        string needle = Filter.Trim();
        VisibleFonts.Clear();
        foreach (FontEntry font in _fonts)
        {
            if ((!MonospaceOnly || font.IsMonospace)
                && (needle.Length == 0 || font.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                VisibleFonts.Add(font);
            }
        }

        SelectedFont = keep is not null && VisibleFonts.Contains(keep) ? keep : null;
    }
}
