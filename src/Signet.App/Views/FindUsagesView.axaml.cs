using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// "Find Usages" panel view (<see cref="FindUsagesViewModel"/>): a left toolbar (Refresh, Group By, Expand All, Collapse
/// All) and the tree of the usages (Usage, Line, Col., Kind, Context); double-clicking (or Enter on) a usage opens the
/// file with the caret on it. Ctrl+NumPad + / Ctrl+NumPad - in the panel expand / collapse all, as in the JetBrains
/// IDEs.
/// </summary>
/// <remarks>
/// The tree is a virtualizing <see cref="TreeDataGrid"/> over <see cref="FindUsagesViewModel.Roots"/>, built here (a
/// TreeDataGrid source is bound to the UI thread). The keys of a tree view are handled here: Right / NumPad + expand the
/// selected node (Right on an expanded one goes to its first child), Left / NumPad - collapse it (Left on a collapsed
/// node or a usage goes to the parent); double-clicking a file or the root expands / collapses it.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Roots it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class FindUsagesView : UserControl
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<FindUsagesNode>? _columns;
    private HierarchicalTreeDataGridSource<FindUsagesNode>? _source;
    private FindUsagesViewModel? _viewModel;

    /// <summary>Initializes the view.</summary>
    public FindUsagesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnViewModelChanged();

        // The width the user gives the "Usage" column by dragging the edge of its header is remembered on the release (also
        // when handled: the resizer of the header handles the pointer).
        UsageList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        // The width that fits is measured in the font of the grid, which is known once the view is in the window (and
        // changes with the font of the interface).
        UsageList.PropertyChanged += OnListPropertyChanged;

        // The cell of a column gets the class of the column, which the styles of its background (the XAML) select.
        UsageList.CellPrepared += (_, e) =>
        {
            MarkColumn(e.Cell, e.ColumnIndex);
            ApplyCellFont(e.Cell, e.ColumnIndex);
        };

        // The headers are made when the columns come into view: they get their font after every layout (a few controls).
        UsageList.LayoutUpdated += (_, _) => ApplyHeaderFonts();
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnThemeChanged;
            ApplyColumnBackgrounds();
        };
        DetachedFromVisualTree += (_, _) => AppTheme.Changed -= OnThemeChanged;
        UsageList.RowPrepared += (_, e) => e.Row.Margin = RowMargin;
        Loaded += (_, _) => ApplyUsageColumnWidth();

        // Tunnel: the tree handles NumPad +/- itself (expanding / collapsing the selected node).
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);

        // Tunnel: before the grid's own key handling.
        UsageList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private FindUsagesNode? SelectedNode => _source?.RowSelection?.SelectedItem;

    // One level of the tree is indented by this much (the default of TreeDataGridExpanderCell.IndentSize).
    private const double IndentSize = 20;

    // The chrome of a row left of the label: the toggle of the tree (a 12 px chevron with a 4 px margin on both sides).
    private const double ExpanderWidth = 20;

    // The room kept after the widest label when the column fits its content.
    private const double FitMargin = 10;

    // The labels measured per level: the ones with the most characters are the widest in practice.
    private const int MeasuredPerLevel = 40;

    private void OnViewModelChanged()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Roots.CollectionChanged -= OnRootsChanged;
            _viewModel.AppearanceChanged -= OnAppearanceChanged;
        }

        _viewModel = DataContext as FindUsagesViewModel;
        BuildTree();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Roots.CollectionChanged += OnRootsChanged;
            _viewModel.AppearanceChanged += OnAppearanceChanged;
            UpdateWrapRows();
            ApplyAppearance();
            ApplyColumnBackgrounds();
            ApplyRowSpacing();
            ApplyUsageColumnWidth();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FindUsagesViewModel.WrapRows):
                UpdateWrapRows();
                break;
            case nameof(FindUsagesViewModel.RowSpacing):
                ApplyRowSpacing();
                break;
            case nameof(FindUsagesViewModel.FitUsageColumn):
                if (_viewModel is { FitUsageColumn: false })
                {
                    RememberUsageColumnWidth(); // the width it has now is the one kept from now on
                }
                else
                {
                    ApplyUsageColumnWidth();
                }

                break;
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        ApplyAppearance();
        ApplyUsageColumnWidth(); // another font, another widest entry
    }

    private FindUsagesAppearance _look = FindUsagesAppearance.Default;

    /// <summary>
    /// Applies the fonts: the column headers, the cells (the "Usage", "Line", "Col." and "Kind" columns), the "Context"
    /// column and the heading above the tree. The values are set on the controls themselves (and cleared again for a font left
    /// to the interface): the cells and the headers are parts of the template of the grid, which neither a style taken away nor
    /// a resource changed reaches in time.
    /// </summary>
    private void ApplyAppearance()
    {
        _look = _viewModel?.Appearance ?? FindUsagesAppearance.Default;
        foreach (TreeDataGridCell cell in UsageList.GetVisualDescendants().OfType<TreeDataGridCell>())
        {
            ApplyCellFont(cell, cell.ColumnIndex);
        }

        ApplyHeaderFonts();

        // The heading is not in the grid: its font is set directly (and cleared to follow the interface again).
        SetFont(HeadingText, _look.CellsFontFamily, _look.CellsFontSize, 0);
        HeadingText.FontWeight = _look.CellsFontWeight > 0 ? FontWeightOptions.ToAvalonia(_look.CellsFontWeight) : FontWeight.SemiBold;
    }

    // The "Context" column (the last one) has the font of its own, the other cells share one.
    private void ApplyCellFont(Control cell, int columnIndex)
    {
        if (columnIndex == ColumnClasses.Length - 1)
        {
            SetFont(cell, _look.ContextFontFamily, _look.ContextFontSize, _look.ContextFontWeight);
        }
        else
        {
            SetFont(cell, _look.CellsFontFamily, _look.CellsFontSize, _look.CellsFontWeight);
        }
    }

    private void ApplyHeaderFonts()
    {
        if (UsageList.ColumnHeadersPresenter is { } presenter)
        {
            foreach (TreeDataGridColumnHeader header in presenter.GetVisualChildren().OfType<TreeDataGridColumnHeader>())
            {
                SetFont(header, _look.HeaderFontFamily, _look.HeaderFontSize, _look.HeaderFontWeight);
            }
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
        else if (control is not TextBlock)
        {
            control.ClearValue(TemplatedControl.FontWeightProperty);
        }
    }

    // The class of a column of the tree: "usageColumn0" for "Usage" … "usageColumn4" for "Context".
    private static readonly string[] ColumnClasses = { "usageColumn0", "usageColumn1", "usageColumn2", "usageColumn3", "usageColumn4" };

    // The theme key of the background of each column, in the order of the columns.
    private static readonly ThemeKey[] ColumnBackgrounds =
    {
        ThemeKeys.UiFindUsagesUsageBackground, ThemeKeys.UiFindUsagesLineBackground, ThemeKeys.UiFindUsagesColumnBackground,
        ThemeKeys.UiFindUsagesKindBackground, ThemeKeys.UiFindUsagesContextBackground,
    };

    private readonly List<Style> _columnStyles = new();

    private static void MarkColumn(Control cell, int columnIndex)
    {
        for (int i = 0; i < ColumnClasses.Length; i++)
        {
            cell.Classes.Set(ColumnClasses[i], i == columnIndex);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyColumnBackgrounds();

    /// <summary>
    /// Gives every column the background its theme key sets (Preferences › Appearance › Interface Colors), not in a selected
    /// row, whose color is the selection's, drawn behind the cells. The style of a column is replaced by a new one, with a
    /// transparent background when the theme sets no color: a style taken away does not give the cells their background back
    /// (they are parts of the template of the grid), a style added later wins over the one before.
    /// </summary>
    private void ApplyColumnBackgrounds()
    {
        foreach (Style old in _columnStyles)
        {
            UsageList.Styles.Remove(old);
        }

        _columnStyles.Clear();
        for (int i = 0; i < ColumnClasses.Length; i++)
        {
            string columnClass = ColumnClasses[i];
            Color? color = AppTheme.Current.Color(ColumnBackgrounds[i]);
            Style style = new(x => x.OfType<TreeDataGridRow>().Not(n => n.Class(":selected")).Descendant().Is<TreeDataGridCell>().Class(columnClass));
            style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, color is { } c ? new SolidColorBrush(c) : Brushes.Transparent));
            _columnStyles.Add(style);
            UsageList.Styles.Add(style);
        }
    }

    // The space between the rows is the bottom margin of a row: it lies outside the cells, so their backgrounds do not
    // reach it and the background of the panel shows between them. Set on the rows as they are prepared (the rows are
    // recycled) and on the realized ones when the setting changes.
    private Thickness RowMargin => new(0, 0, 0, _viewModel?.RowSpacing ?? 0);

    private void ApplyRowSpacing()
    {
        foreach (TreeDataGridRow row in UsageList.GetVisualDescendants().OfType<TreeDataGridRow>())
        {
            row.Margin = RowMargin;
        }

        UsageList.RowsPresenter?.InvalidateMeasure();
    }

    private void OnListPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name is "FontFamily" or "FontSize" or "FontStyle" or "FontWeight")
        {
            ApplyUsageColumnWidth();
        }
    }

    // A new search (or a refresh, a grouping, a language switch) puts a new root in: the widest entry may be another.
    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ApplyUsageColumnWidth();
        }
    }

    // The class "wrapRows" of the grid makes the text of the cells wrap (see the styles); the rows are laid out again.
    private void UpdateWrapRows()
    {
        UsageList.Classes.Set("wrapRows", _viewModel?.WrapRows == true);
        UsageList.RowsPresenter?.InvalidateMeasure();
    }

    // Fits the "Usage" column to its widest entry, or restores the width the user gave it (nothing to do until one was set).
    private void ApplyUsageColumnWidth()
    {
        if (_source is null || _viewModel is null)
        {
            return;
        }

        double width = _viewModel.FitUsageColumn ? MeasureUsageColumn() : _viewModel.UsageColumnWidth;
        if (width > 0)
        {
            _source.Columns.SetColumnWidth(0, new GridLength(Math.Ceiling(width), GridUnitType.Pixel));
        }
    }

    // The width of the widest label of the tree (all nodes, expanded or not) with its indentation, the toggle and a margin.
    private double MeasureUsageColumn()
    {
        if (_viewModel is null || _viewModel.Roots.Count == 0)
        {
            return 0;
        }

        List<FindUsagesNode> all = new();
        Stack<FindUsagesNode> pending = new(_viewModel.Roots);
        while (pending.TryPop(out FindUsagesNode? node))
        {
            all.Add(node);
            foreach (FindUsagesNode child in node.Children)
            {
                pending.Push(child);
            }
        }

        FindUsagesAppearance look = _viewModel.Appearance;
        FontFamily family = look.CellsFontFamily.Length > 0 ? new FontFamily(look.CellsFontFamily) : UsageList.FontFamily;
        double fontSize = look.CellsFontSize > 0 ? look.CellsFontSize : UsageList.FontSize;
        Typeface typeface = new(family, UsageList.FontStyle, UsageList.FontWeight);
        double widest = 0;
        foreach (IGrouping<int, FindUsagesNode> level in all.GroupBy(n => n.Level))
        {
            foreach (FindUsagesNode node in level.OrderByDescending(n => n.Text.Length + n.CountSuffix.Length).Take(MeasuredPerLevel))
            {
                FormattedText text = new(
                    node.Text + node.CountSuffix, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, null);
                widest = Math.Max(widest, (level.Key * IndentSize) + ExpanderWidth + text.Width);
            }
        }

        return widest + FitMargin;
    }

    // What the user set by dragging the edge of the header is remembered on the release (a fitted width is not theirs).
    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is { FitUsageColumn: false })
        {
            RememberUsageColumnWidth();
        }
    }

    private void RememberUsageColumnWidth()
    {
        if (_viewModel is not null && _source?.Columns[0] is { Width.IsAbsolute: true, ActualWidth: > 0 } column
            && (int)Math.Round(column.ActualWidth) != _viewModel.UsageColumnWidth)
        {
            _viewModel.UsageColumnWidth = (int)Math.Round(column.ActualWidth);
        }
    }

    private void BuildTree()
    {
        _source?.Dispose();
        _source = null;
        if (DataContext is FindUsagesViewModel vm)
        {
            _columns = new LocalizedColumns<FindUsagesNode>();
            _source = new HierarchicalTreeDataGridSource<FindUsagesNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template(
                            "FindUsages_ColumnUsage",
                            "UsageCellTemplate",
                            new GridLength(2, GridUnitType.Star),
                            new TemplateColumnOptions<FindUsagesNode>
                            {
                                CompareAscending = FindUsagesNode.CompareByLocation,
                                CompareDescending = (a, b) => FindUsagesNode.CompareByLocation(b, a),
                            }),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Text("ValidationResultsView_Line", n => n.Line, new GridLength(70)),
                    _columns.Text("FindUsages_ColumnColumn", n => n.Column, new GridLength(60)),
                    _columns.Text("FindUsages_ColumnKind", n => n.KindText, new GridLength(120)),
                    _columns.Template(
                        "FindUsages_ColumnContext",
                        "ContextCellTemplate",
                        new GridLength(3, GridUnitType.Star),
                        new TemplateColumnOptions<FindUsagesNode>
                        {
                            CompareAscending = (a, b) => string.CompareOrdinal(a?.Context, b?.Context),
                            CompareDescending = (a, b) => string.CompareOrdinal(b?.Context, a?.Context),
                        }),
                },
            };
        }

        UsageList.Source = _source;
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { HasChildren: true } node)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        ActivateSelected();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || SelectedNode is not { } node)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = ActivateSelected();
                break;
            case Key.Right or Key.Add when node.HasChildren:
                if (node.IsExpanded && e.Key == Key.Right)
                {
                    Select(node.Children[0]);
                }

                node.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Left or Key.Subtract:
                if (node is { HasChildren: true, IsExpanded: true })
                {
                    node.IsExpanded = false;
                }
                else if (e.Key == Key.Left && node.Parent is { } parent)
                {
                    Select(parent);
                }

                e.Handled = true;
                break;
        }
    }

    // Selects a node (by its index path), scrolls it into view and moves the keyboard focus to it, so that Up / Down
    // continue from the new row.
    private void Select(FindUsagesNode node)
    {
        if (_source?.RowSelection is not { } selection)
        {
            return;
        }

        List<int> path = new();
        for (FindUsagesNode current = node; current.Parent is { } parent; current = parent)
        {
            path.Insert(0, IndexOf(parent.Children, current));
        }

        path.Insert(0, 0);
        IndexPath index = new(path);
        selection.SelectedIndex = index;
        if (UsageList.Rows?.ModelIndexToRowIndex(index) is >= 0 and var row)
        {
            UsageList.RowsPresenter?.BringIntoView(row);
            (UsageList.TryGetRow(row)?.TryGetCell(0) as Control)?.Focus(NavigationMethod.Directional);
        }
    }

    private static int IndexOf(IReadOnlyList<FindUsagesNode> nodes, FindUsagesNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Add or Key.Subtract
            && DataContext is FindUsagesViewModel vm)
        {
            IRelayCommand command = e.Key == Key.Add ? vm.ExpandAllCommand : vm.CollapseAllCommand;
            command.Execute(null);
            e.Handled = true;
        }
    }

    private bool ActivateSelected()
    {
        if (DataContext is FindUsagesViewModel vm && SelectedNode is { Usage: not null } node)
        {
            vm.Activate(node);
            return true;
        }

        return false;
    }
}
