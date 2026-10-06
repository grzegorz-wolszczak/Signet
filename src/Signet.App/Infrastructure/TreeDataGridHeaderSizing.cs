using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;

namespace Signet.App.Infrastructure;

/// <summary>
/// Makes the header text of every <see cref="TreeDataGrid"/> column fully visible (as Signet's former DataGrid tables
/// did): once the grid is shown (and again when a header changes, e.g. after a language
/// switch) each column gets a minimum width equal to the width its header needs, so the header is not cut off and a
/// column cannot be made narrower than its header. Enabled for all grids by a style in <c>App.axaml</c>; applies to
/// the columns created by <see cref="LocalizedColumns{TModel}"/> (they register their minimum width here).
/// </summary>
public static class TreeDataGridHeaderSizing
{
    /// <summary>Whether the minimum width of the columns follows their headers.</summary>
    public static readonly AttachedProperty<bool> FitHeadersProperty =
        AvaloniaProperty.RegisterAttached<TreeDataGrid, bool>("FitHeaders", typeof(TreeDataGridHeaderSizing));

    // The minimum width accessors of the registered columns and their own lower bound (before fitting).
    private static readonly ConditionalWeakTable<IColumn, ColumnMinWidth> Columns = new();

    // Headers already watched for header changes.
    private static readonly ConditionalWeakTable<TreeDataGridColumnHeader, object> WatchedHeaders = new();

    static TreeDataGridHeaderSizing()
    {
        FitHeadersProperty.Changed.AddClassHandler<TreeDataGrid>(OnFitHeadersChanged);
    }

    /// <summary>Gets <see cref="FitHeadersProperty"/>.</summary>
    public static bool GetFitHeaders(TreeDataGrid grid) => grid.GetValue(FitHeadersProperty);

    /// <summary>Sets <see cref="FitHeadersProperty"/>.</summary>
    public static void SetFitHeaders(TreeDataGrid grid, bool value) => grid.SetValue(FitHeadersProperty, value);

    /// <summary>Registers how to read and change the minimum width of <paramref name="column"/>.</summary>
    public static void Register(IColumn column, Func<GridLength> getMinWidth, Action<GridLength> setMinWidth)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(getMinWidth);
        ArgumentNullException.ThrowIfNull(setMinWidth);
        GridLength original = getMinWidth();
        Columns.AddOrUpdate(column, new ColumnMinWidth(
            getMinWidth, setMinWidth, original.IsAbsolute ? original.Value : 0));
    }

    /// <summary>Fits the minimum width of every registered column of <paramref name="grid"/> to its header now.</summary>
    public static void Fit(TreeDataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        bool changed = false;
        foreach (TreeDataGridColumnHeader header in grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>())
        {
            if (WatchedHeaders.TryAdd(header, new object()))
            {
                header.PropertyChanged += (_, e) =>
                {
                    if (e.Property == TreeDataGridColumnHeader.HeaderProperty)
                    {
                        Dispatcher.UIThread.Post(() => Fit(grid), DispatcherPriority.Background);
                    }
                };
            }

            if (grid.Columns is not { } columns
                || header.ColumnIndex < 0
                || header.ColumnIndex >= columns.Count
                || !Columns.TryGetValue(columns[header.ColumnIndex], out ColumnMinWidth? minWidth))
            {
                continue;
            }

            header.Measure(Size.Infinity);
            double needed = Math.Ceiling(header.DesiredSize.Width) + 2;
            double fitted = Math.Max(minWidth.Original, needed);
            GridLength current = minWidth.Get();
            if (!current.IsAbsolute || Math.Abs(current.Value - fitted) > 0.5)
            {
                minWidth.Set(new GridLength(fitted, GridUnitType.Pixel));
                changed = true;
            }
        }

        if (changed)
        {
            foreach (Control presenter in grid.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is TreeDataGridColumnHeadersPresenter or TreeDataGridRowsPresenter or TreeDataGridCellsPresenter))
            {
                presenter.InvalidateMeasure();
            }

            grid.InvalidateMeasure();
        }
    }

    private static void OnFitHeadersChanged(TreeDataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            grid.Loaded += OnLoaded;
            if (grid.IsLoaded)
            {
                Dispatcher.UIThread.Post(() => Fit(grid), DispatcherPriority.Background);
            }
        }
        else
        {
            grid.Loaded -= OnLoaded;
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TreeDataGrid grid)
        {
            // After the first layout pass, when the column headers exist.
            Dispatcher.UIThread.Post(() => Fit(grid), DispatcherPriority.Background);
        }
    }

    private sealed record ColumnMinWidth(Func<GridLength> Get, Action<GridLength> Set, double Original);
}
