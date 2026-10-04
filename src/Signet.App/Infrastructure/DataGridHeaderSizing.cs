using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Signet.App.Infrastructure;

/// <summary>
/// Makes the header text of every <see cref="DataGrid"/> column fully visible: once the grid is shown (and again when
/// a header text changes, e.g. after a language switch) each column gets a minimum width equal to the width its
/// header needs, so the header is not cut off by default and a column cannot be made narrower than its header.
/// Enabled for all grids by a style in <c>App.axaml</c> (together with resizable columns).
/// </summary>
public static class DataGridHeaderSizing
{
    /// <summary>Whether the minimum width of the columns follows their headers.</summary>
    public static readonly AttachedProperty<bool> FitHeadersProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("FitHeaders", typeof(DataGridHeaderSizing));

    // The minimum width a column had before it was fitted to its header (its own lower bound).
    private static readonly ConditionalWeakTable<DataGridColumn, StrongBox<double>> OriginalMinWidths = new();

    // Headers already watched for text changes.
    private static readonly ConditionalWeakTable<DataGridColumnHeader, object> WatchedHeaders = new();

    static DataGridHeaderSizing()
    {
        FitHeadersProperty.Changed.AddClassHandler<DataGrid>(OnFitHeadersChanged);
    }

    /// <summary>Gets <see cref="FitHeadersProperty"/>.</summary>
    public static bool GetFitHeaders(DataGrid grid) => grid.GetValue(FitHeadersProperty);

    /// <summary>Sets <see cref="FitHeadersProperty"/>.</summary>
    public static void SetFitHeaders(DataGrid grid, bool value) => grid.SetValue(FitHeadersProperty, value);

    /// <summary>Fits the minimum width of every column of <paramref name="grid"/> to its header now.</summary>
    public static void Fit(DataGrid grid)
    {
        foreach (DataGridColumnHeader header in grid.GetVisualDescendants().OfType<DataGridColumnHeader>())
        {
            if (WatchedHeaders.TryAdd(header, new object()))
            {
                header.PropertyChanged += (_, e) =>
                {
                    if (e.Property == ContentControl.ContentProperty)
                    {
                        Dispatcher.UIThread.Post(() => Fit(grid), DispatcherPriority.Background);
                    }
                };
            }

            if (header.Content is null
                || grid.Columns.FirstOrDefault(c => Equals(c.Header, header.Content)) is not { } column)
            {
                continue;
            }

            double original = OriginalMinWidths.GetValue(column, c => new StrongBox<double>(c.MinWidth)).Value;
            header.Measure(Size.Infinity);
            double needed = System.Math.Ceiling(header.DesiredSize.Width) + 2;
            double minWidth = System.Math.Max(original, needed);
            if (System.Math.Abs(column.MinWidth - minWidth) > 0.5)
            {
                column.MinWidth = minWidth;
            }
        }

        grid.InvalidateMeasure();
    }

    private static void OnFitHeadersChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
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
        if (sender is DataGrid grid)
        {
            // After the first layout pass, when the header cells exist.
            Dispatcher.UIThread.Post(() => Fit(grid), DispatcherPriority.Background);
        }
    }
}
