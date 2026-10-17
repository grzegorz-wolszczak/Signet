using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

        // The cell of a column gets the class of the column, which the styles of the column backgrounds select.
        UsageList.CellPrepared += (_, e) => MarkColumn(e.Cell, e.ColumnIndex);
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnThemeChanged;
            ApplyColumnBackgrounds();
        };
        DetachedFromVisualTree += (_, _) => AppTheme.Changed -= OnThemeChanged;
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

    // The styles of the panel that follow the fonts of Preferences › Appearance and Behavior › Find Usages Window.
    private Style[] _appearanceStyles = Array.Empty<Style>();

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        ApplyAppearance();
        ApplyUsageColumnWidth(); // another font, another widest entry
    }

    /// <summary>
    /// Applies the fonts: the column headers, the cells (the "Usage", "Line", "Col." and "Kind" columns), the "Context"
    /// column (a style of its own, so it wins over the font of the cells) and the heading above the tree. A font left to the
    /// interface keeps the interface font (nothing is set). The styles of the previous fonts are replaced.
    /// </summary>
    private void ApplyAppearance()
    {
        foreach (Style old in _appearanceStyles)
        {
            UsageList.Styles.Remove(old);
        }

        _appearanceStyles = Array.Empty<Style>();
        if (_viewModel is null)
        {
            return;
        }

        FindUsagesAppearance look = _viewModel.Appearance;
        Style headers = new(x => x.OfType<TreeDataGridColumnHeader>());
        AddFont(headers, look.HeaderFontFamily, look.HeaderFontSize);
        Style cells = new(x => x.Is<TreeDataGridCell>());
        AddFont(cells, look.CellsFontFamily, look.CellsFontSize);
        Style context = new(x => x.OfType<TextBlock>().Class("usageContext")); // the control keeps the style key of TextBlock
        AddFont(context, look.ContextFontFamily, look.ContextFontSize);
        _appearanceStyles = new[] { headers, cells, context };
        foreach (Style style in _appearanceStyles)
        {
            UsageList.Styles.Add(style);
        }

        // The heading is not in the grid: its font is set directly (and cleared to follow the interface again).
        HeadingText.ClearValue(TextBlock.FontFamilyProperty);
        HeadingText.ClearValue(TextBlock.FontSizeProperty);
        if (look.CellsFontFamily.Length > 0)
        {
            HeadingText.FontFamily = new FontFamily(look.CellsFontFamily);
        }

        if (look.CellsFontSize > 0)
        {
            HeadingText.FontSize = look.CellsFontSize;
        }
    }

    private static void AddFont(Style style, string family, int size)
    {
        if (family.Length > 0)
        {
            style.Setters.Add(new Setter(TextElement.FontFamilyProperty, new FontFamily(family)));
        }

        if (size > 0)
        {
            style.Setters.Add(new Setter(TextElement.FontSizeProperty, (double)size));
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

    private Style[] _columnStyles = Array.Empty<Style>();

    private static void MarkColumn(Control cell, int columnIndex)
    {
        for (int i = 0; i < ColumnClasses.Length; i++)
        {
            cell.Classes.Set(ColumnClasses[i], i == columnIndex);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyColumnBackgrounds();

    /// <summary>
    /// Gives every column the background its theme key sets (Preferences › Appearance › Interface Colors); a column without
    /// one stays transparent. A selected row keeps the color of the selection.
    /// </summary>
    private void ApplyColumnBackgrounds()
    {
        foreach (Style old in _columnStyles)
        {
            UsageList.Styles.Remove(old);
        }

        List<Style> styles = new();
        for (int i = 0; i < ColumnClasses.Length; i++)
        {
            if (AppTheme.Current.Color(ColumnBackgrounds[i]) is not { } color)
            {
                continue;
            }

            string columnClass = ColumnClasses[i];
            // Not in a selected row: its color is the selection's, drawn behind the cells.
            Style style = new(x => x.OfType<TreeDataGridRow>().Not(n => n.Class(":selected")).Descendant().Is<TreeDataGridCell>().Class(columnClass));
            style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(color)));
            styles.Add(style);
            UsageList.Styles.Add(style);
        }

        _columnStyles = styles.ToArray();
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
