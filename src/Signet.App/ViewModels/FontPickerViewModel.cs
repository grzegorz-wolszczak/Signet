using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Infrastructure;

namespace Signet.App.ViewModels;

/// <summary>A request to pick a font from Preferences.</summary>
/// <param name="Family">Current family (may be a fallback list "A, B" — the first one counts).</param>
/// <param name="Size">Current size, or <see langword="null"/> when the window should pick only the typeface.</param>
/// <param name="MonospaceOnly">Whether the "fixed-width only" filter should start enabled.</param>
public sealed record FontPickRequest(string Family, int? Size, bool MonospaceOnly);

/// <summary>The result of the font picker window.</summary>
/// <param name="Family">Selected family.</param>
/// <param name="Size">Selected size (irrelevant when the request did not include a size).</param>
public sealed record FontPickResult(string Family, int Size);

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

    /// <summary>Creates the model over the given font list (in the application: <see cref="FontCatalog.SystemFonts"/>).</summary>
    public FontPickerViewModel(IReadOnlyList<FontEntry> fonts, FontPickRequest request)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(request);
        _fonts = fonts;

        ShowSize = request.Size is not null;
        _size = Math.Clamp(request.Size ?? 12, MinSize, MaxSize);

        string current = request.Family.Split(',')[0].Trim().Trim('"', '\'');
        FontEntry? initial = fonts.FirstOrDefault(f => string.Equals(f.Name, current, StringComparison.OrdinalIgnoreCase));

        // The current font must be visible and selected — if the monospace filter would hide it, it starts disabled.
        _monospaceOnly = request.MonospaceOnly && (initial is null || initial.IsMonospace);
        ApplyFilter();
        SelectedFont = initial is not null && VisibleFonts.Contains(initial) ? initial : null;
    }

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

    /// <summary>Selected size (as <see cref="decimal"/> for <c>NumericUpDown</c>).</summary>
    [ObservableProperty]
    private decimal _size;

    /// <summary>Whether the choice can be accepted (a font is selected).</summary>
    public bool CanAccept => SelectedFont is not null;

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnMonospaceOnlyChanged(bool value) => ApplyFilter();

    /// <summary>The result to return after OK, or <see langword="null"/> when nothing is selected.</summary>
    public FontPickResult? Result() =>
        SelectedFont is { } font
            ? new FontPickResult(font.Name, (int)Math.Clamp(Math.Round(Size), MinSize, MaxSize))
            : null;

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
