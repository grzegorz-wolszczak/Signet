using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The words table of the Spelling Editor: the unique words of the book, each word that occurs more than once with a
/// row per occurrence under it. Used by <see cref="SpellcheckEditorWindow"/> and by the preview of
/// Preferences › Appearance › Spelling Editor, so that both show the same table in the same look.
/// </summary>
/// <remarks>
/// The source and the columns are built here, over the view model's <see cref="SpellcheckEditorViewModel.Words"/> (a
/// TreeDataGrid source is bound to the UI thread, so it does not belong in the view model). Several rows can be
/// selected; the selection is passed to <see cref="SpellcheckEditorViewModel.SetSelectedWords"/>.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Words it observes.")]
public partial class SpellcheckWordsView : UserControl
{
    private SpellcheckEditorViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<SpellcheckWordRow>? _columns;
    private HierarchicalTreeDataGridSource<SpellcheckWordRow>? _source;

    /// <summary>Initializes the view.</summary>
    public SpellcheckWordsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        WordsGrid.DoubleTapped += OnWordDoubleTapped;

        // A cell is given its font as it is prepared (the rows are virtualized, so the ones made later need it too).
        WordsGrid.CellPrepared += (_, e) => ApplyCellFont(e.Cell, e.ColumnIndex);
    }

    /// <summary>
    /// Whether the table only shows the look (the preview of Preferences): it takes no clicks, so nothing is selected
    /// and nothing is navigated to.
    /// </summary>
    public bool IsSample { get; set; }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_source is not null)
        {
            _source.RowSelection!.SelectionChanged -= OnSelectionChanged;
            _source.Dispose();
            _source = null;
        }

        if (_bound is not null)
        {
            _bound.SelectRowRequested -= OnSelectRowRequested;
            _bound.AppearanceChanged -= OnAppearanceChanged;
        }

        _bound = DataContext as SpellcheckEditorViewModel;
        if (_bound is not null)
        {
            _bound.SelectRowRequested += OnSelectRowRequested;
            _bound.AppearanceChanged += OnAppearanceChanged;
            _columns = new LocalizedColumns<SpellcheckWordRow>();

            // A word that occurs more than once has one child row per occurrence (file and line), so that each can be
            // gone to and replaced on its own; a word that occurs once is its own occurrence and has no expander.
            _source = new HierarchicalTreeDataGridSource<SpellcheckWordRow>(_bound.Words)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Text("SpellcheckEditorWindow_Word", r => r.Display, new GridLength(1, GridUnitType.Star)),
                        r => r.Occurrences,
                        r => r.Occurrences.Count > 0),
                    _columns.Text("ReportsWindow_Count", r => r.CountDisplay, new GridLength(70)),
                    _columns.Text("SpellcheckEditorWindow_Language", r => r.LanguageDisplay, new GridLength(110)),
                    _columns.Text("SpellcheckEditorWindow_Misspelled", r => r.MisspelledDisplay, new GridLength(90)),
                },
            };
            _source.RowSelection!.SingleSelect = false;
            _source.RowSelection.SelectionChanged += OnSelectionChanged;
        }

        WordsGrid.Source = _source;
        ApplyAppearance();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed += OnThemeChanged;
        ApplyAppearance();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AppTheme.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyAppearance();

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    /// <summary>
    /// Applies the look of the table (Preferences › Appearance › Spelling Editor): the font of the column headers, of
    /// the "Word" column and of the other cells, and the background the theme gives the column headers.
    /// </summary>
    public void ApplyAppearance()
    {
        foreach (TreeDataGridCell cell in WordsGrid.GetVisualDescendants().OfType<TreeDataGridCell>())
        {
            ApplyCellFont(cell, cell.ColumnIndex);
        }

        if (WordsGrid.ColumnHeadersPresenter is not { } presenter)
        {
            return;
        }

        SpellcheckEditorAppearance look = _bound?.Appearance ?? SpellcheckEditorAppearance.Default;
        IBrush? background = AppTheme.Current.Color(ThemeKeys.UiSpellcheckEditorHeaderBackground) is { } color
            ? new SolidColorBrush(color)
            : null;
        foreach (TreeDataGridColumnHeader header in presenter.GetVisualChildren().OfType<TreeDataGridColumnHeader>())
        {
            SetFont(header, look.HeaderFontFamily, look.HeaderFontSize, look.HeaderFontWeight);
            if (background is not null)
            {
                header.SetValue(TemplatedControl.BackgroundProperty, background);
            }
            else
            {
                header.ClearValue(TemplatedControl.BackgroundProperty);
            }
        }
    }

    // The "Word" column (the first one) has a font of its own; the rest share one.
    private void ApplyCellFont(Control cell, int columnIndex)
    {
        SpellcheckEditorAppearance look = _bound?.Appearance ?? SpellcheckEditorAppearance.Default;
        if (columnIndex == 0)
        {
            SetFont(cell, look.WordFontFamily, look.WordFontSize, look.WordFontWeight);
        }
        else
        {
            SetFont(cell, look.CellsFontFamily, look.CellsFontSize, look.CellsFontWeight);
        }
    }

    // A font setting that is empty / 0 clears the value, so the control follows the interface font again.
    private static void SetFont(Control control, string family, int size, int weight)
    {
        if (family.Length > 0)
        {
            control.SetValue(TemplatedControl.FontFamilyProperty, new FontFamily(family));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontFamilyProperty);
        }

        if (size > 0)
        {
            control.SetValue(TemplatedControl.FontSizeProperty, (double)size);
        }
        else
        {
            control.ClearValue(TemplatedControl.FontSizeProperty);
        }

        if (weight > 0)
        {
            control.SetValue(TemplatedControl.FontWeightProperty, FontWeightOptions.ToAvalonia(weight));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontWeightProperty);
        }
    }

    private void OnWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsSample && _bound is not null && _source?.RowSelection?.SelectedItem is { } row)
        {
            _bound.RequestNavigation(row);
        }
    }

    private void OnSelectRowRequested(object? sender, int index)
    {
        if (_source?.RowSelection is not { } selection)
        {
            return;
        }

        selection.Clear();
        selection.Select(new IndexPath(index));
        WordsGrid.RowsPresenter?.BringIntoView(index);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (!IsSample && _source is not null)
        {
            _bound?.SetSelectedWords(_source.RowSelection!.SelectedItems.OfType<SpellcheckWordRow>());
        }
    }
}
