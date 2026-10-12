using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.Fonts;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>A stylesheet the <c>@font-face</c> rules can be written to — or a new <c>fonts.css</c>.</summary>
/// <param name="Stylesheet">The stylesheet, or <c>null</c> for a new one.</param>
/// <param name="Display">The text in the list.</param>
public sealed record FontFaceStylesheetOption(CssResource? Stylesheet, string Display)
{
    /// <inheritdoc/>
    public override string ToString() => Display;
}

/// <summary>What the "Add @font-face rules" dialog decided.</summary>
/// <param name="Stylesheet">The stylesheet to write to, or <c>null</c> for a new <c>fonts.css</c>.</param>
/// <param name="Requests">The rules to write.</param>
/// <param name="BodyFamily">The family to set on <c>body</c>, or <c>null</c>.</param>
/// <param name="BodyGeneric">The generic family after it.</param>
public sealed record FontFaceDialogResult(
    CssResource? Stylesheet,
    IReadOnlyList<FontFaceRequest> Requests,
    string? BodyFamily,
    string BodyGeneric);

/// <summary>
/// The "Add @font-face rules" dialog: one row per font file with the descriptors read from the file (the family
/// editable), the duplicates left out with the reason, warnings about restricted embedding and formats, the target
/// stylesheet (the first one by default), an optional font for <c>body</c> and a preview of the CSS.
/// </summary>
public sealed partial class FontFaceDialogViewModel : ObservableObject
{
    private readonly BookFontIndex _index;
    private bool _recomputing;

    /// <summary>Creates the dialog for <paramref name="candidates"/>; <paramref name="stylesheets"/> in book order.</summary>
    public FontFaceDialogViewModel(BookFontIndex index, IReadOnlyList<FontFaceCandidate> candidates, IReadOnlyList<CssResource> stylesheets)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(stylesheets);
        _index = index;
        Stylesheets = stylesheets
            .Select(css => new FontFaceStylesheetOption(css, css.BookPath))
            .Append(new FontFaceStylesheetOption(null, Strings.Get("FontFaceDialog_NewStylesheet")))
            .ToList();
        _selectedStylesheet = Stylesheets[0];
        foreach (FontFaceCandidate candidate in candidates)
        {
            FontFaceRowViewModel row = new(candidate);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        Recompute();
        _bodyFamily = BodyFamilies.FirstOrDefault();
        _bodyGeneric = BodyGenericFor(_bodyFamily);
    }

    /// <summary>The font files.</summary>
    public ObservableCollection<FontFaceRowViewModel> Rows { get; } = new();

    /// <summary>The stylesheets to choose from (the last option creates a new one).</summary>
    public IReadOnlyList<FontFaceStylesheetOption> Stylesheets { get; }

    /// <summary>The families of the rows that will be written (for the <c>body</c> font).</summary>
    public ObservableCollection<string> BodyFamilies { get; } = new();

    /// <summary>The generic families to fall back to.</summary>
    public IReadOnlyList<string> GenericFamilies { get; } = new[] { "serif", "sans-serif", "monospace", "cursive", "fantasy" };

    /// <summary>The stylesheet the rules go to.</summary>
    [ObservableProperty]
    private FontFaceStylesheetOption _selectedStylesheet;

    /// <summary>Whether <c>body</c> gets the font too.</summary>
    [ObservableProperty]
    private bool _setBodyFont;

    /// <summary>The family for <c>body</c>.</summary>
    [ObservableProperty]
    private string? _bodyFamily;

    /// <summary>The generic family after it.</summary>
    [ObservableProperty]
    private string _bodyGeneric;

    /// <summary>The CSS that will be written.</summary>
    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>A summary: how many rules will be written and how many are left out.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Whether there is anything to write.</summary>
    [ObservableProperty]
    private bool _canAdd;

    /// <summary>The decision, for the caller after "Add".</summary>
    public FontFaceDialogResult BuildResult() => new(
        SelectedStylesheet.Stylesheet,
        Requests(),
        SetBodyFont && !string.IsNullOrWhiteSpace(BodyFamily) ? BodyFamily.Trim() : null,
        BodyGeneric);

    partial void OnSelectedStylesheetChanged(FontFaceStylesheetOption value) => UpdatePreview();

    partial void OnSetBodyFontChanged(bool value) => UpdatePreview();

    partial void OnBodyFamilyChanged(string? value)
    {
        BodyGeneric = BodyGenericFor(value);
        UpdatePreview();
    }

    partial void OnBodyGenericChanged(string value) => UpdatePreview();

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_recomputing && e.PropertyName is nameof(FontFaceRowViewModel.Family) or nameof(FontFaceRowViewModel.Weight)
                or nameof(FontFaceRowViewModel.Style) or nameof(FontFaceRowViewModel.IsChecked))
        {
            Recompute();
        }
    }

    // Conflicts depend on the rows above (a face written once is a duplicate further down).
    private void Recompute()
    {
        _recomputing = true;
        try
        {
            List<FontFaceRequest> earlier = new();
            foreach (FontFaceRowViewModel row in Rows)
            {
                FontFaceRequest request = row.ToRequest();
                row.Conflict = string.IsNullOrWhiteSpace(row.Family)
                    ? FontFaceConflict.None
                    : FontFaceAddition.FindConflict(_index, request, earlier);
                if (row.IsIncluded)
                {
                    earlier.Add(request);
                }
            }

            List<string> families = Rows.Where(r => r.IsIncluded).Select(r => r.Family.Trim())
                .Concat(Rows.Where(r => r.Conflict.ExistingRule is not null).Select(r => r.Conflict.ExistingRule!.Family))
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!families.SequenceEqual(BodyFamilies))
            {
                string? current = BodyFamily;
                BodyFamilies.Clear();
                foreach (string family in families)
                {
                    BodyFamilies.Add(family);
                }

                BodyFamily = current is not null && families.Contains(current, StringComparer.OrdinalIgnoreCase) ? current : families.FirstOrDefault();
            }
        }
        finally
        {
            _recomputing = false;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        List<FontFaceRequest> requests = Requests();
        string cssBookPath = SelectedStylesheet.Stylesheet?.BookPath ?? "OEBPS/Styles/fonts.css";
        List<string> blocks = requests.Select(r => FontFaceAddition.BuildRule(r, cssBookPath)).ToList();
        bool body = SetBodyFont && !string.IsNullOrWhiteSpace(BodyFamily);
        if (body)
        {
            blocks.Add(string.Concat("body {\n  font-family: ", FontFamilyList.Quote(BodyFamily!.Trim()), ", ", BodyGeneric, ";\n}"));
        }

        PreviewText = string.Join("\n\n", blocks);
        int skipped = Rows.Count(r => r.IsConflict);
        Summary = Strings.Format("FontFaceDialog_Summary", requests.Count, skipped);
        CanAdd = requests.Count > 0 || body;
    }

    private List<FontFaceRequest> Requests() => Rows.Where(r => r.IsIncluded).Select(r => r.ToRequest()).ToList();

    private string BodyGenericFor(string? family) =>
        Rows.FirstOrDefault(r => string.Equals(r.Family.Trim(), family, StringComparison.OrdinalIgnoreCase))?.Candidate.Generic ?? "serif";
}

/// <summary>A font file of the "Add @font-face rules" dialog.</summary>
public sealed partial class FontFaceRowViewModel : ObservableObject
{
    /// <summary>Creates the row with the descriptors suggested by the file.</summary>
    public FontFaceRowViewModel(FontFaceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        _family = candidate.Suggested.Family;
        _weight = candidate.Suggested.Weight;
        _style = candidate.Suggested.Style;
        List<string> warnings = new();
        if (candidate.EmbeddingRestricted)
        {
            warnings.Add(Strings.Get("FontFaceDialog_EmbeddingRestricted"));
        }

        if (candidate.FormatWarning == FontFormatWarning.NotInEpub2)
        {
            warnings.Add(Strings.Get("FontFaceDialog_FormatNotInEpub2"));
        }
        else if (candidate.FormatWarning == FontFormatWarning.Woff2)
        {
            warnings.Add(Strings.Get("FontFaceDialog_FormatWoff2"));
        }

        if (candidate.Info.Format == FontFileFormat.Unknown)
        {
            warnings.Add(Strings.Get("FontFaceDialog_Unreadable"));
        }

        WarningText = string.Join("\n", warnings);
    }

    /// <summary>The file and what it says about itself.</summary>
    public FontFaceCandidate Candidate { get; }

    /// <summary>The file name.</summary>
    public string FileName => Candidate.FileName;

    /// <summary>The styles to choose from.</summary>
    public IReadOnlyList<string> Styles { get; } = new[] { "normal", "italic", "oblique" };

    /// <summary>The family (editable).</summary>
    [ObservableProperty]
    private string _family;

    /// <summary>The <c>font-weight</c> value (editable).</summary>
    [ObservableProperty]
    private string _weight;

    /// <summary>The <c>font-style</c> value.</summary>
    [ObservableProperty]
    private string _style;

    /// <summary>Whether the rule is written (unless it is a duplicate).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIncluded))]
    private bool _isChecked = true;

    /// <summary>Why the rule is not needed, if it is a duplicate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConflict), nameof(IsIncluded), nameof(ConflictText))]
    private FontFaceConflict _conflict = FontFaceConflict.None;

    /// <summary>Whether the rule is a duplicate (left out).</summary>
    public bool IsConflict => Conflict.Kind != FontFaceConflictKind.None;

    /// <summary>Whether the rule will be written.</summary>
    public bool IsIncluded => IsChecked && !IsConflict && !string.IsNullOrWhiteSpace(Family);

    /// <summary>Why the rule is left out (empty when it is not).</summary>
    public string ConflictText => Conflict switch
    {
        { Kind: FontFaceConflictKind.SameFile, ExistingRule: { } rule } => Strings.Format("FontFaceDialog_ConflictSameFile", rule.BookPath),
        { Kind: FontFaceConflictKind.SameFace, ExistingRule: { } rule } => Strings.Format(
            "FontFaceDialog_ConflictSameFace", FontFaceDescriptors.Describe(rule.Weight, rule.Style), rule.BookPath),
        { Kind: FontFaceConflictKind.SameFace, EarlierFontBookPath: { } earlier } => Strings.Format(
            "FontFaceDialog_ConflictInBatch", System.IO.Path.GetFileName(earlier)),
        _ => string.Empty,
    };

    /// <summary>Warnings about the file (restricted embedding, format), one per line.</summary>
    public string WarningText { get; }

    /// <summary>Whether there are warnings.</summary>
    public bool HasWarning => WarningText.Length > 0;

    /// <summary>The rule this row asks for.</summary>
    public FontFaceRequest ToRequest() =>
        new(Candidate.BookPath, Family.Trim(), string.IsNullOrWhiteSpace(Weight) ? "normal" : Weight.Trim(), Style);
}
