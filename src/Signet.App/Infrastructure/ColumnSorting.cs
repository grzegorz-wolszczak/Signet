using System;
using System.Collections.Generic;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Infrastructure;

/// <summary>Sorting of <c>TreeDataGrid</c> columns by a key other than the displayed value.</summary>
public static class ColumnSorting
{
    /// <summary>
    /// Makes a text column sort by <paramref name="key"/> instead of its displayed value (e.g. a number shown as text,
    /// with "N/A" mapped to a key that sorts last). Returns <paramref name="options"/>.
    /// </summary>
    public static TextColumnOptions<TModel> SortedBy<TModel, TKey>(this TextColumnOptions<TModel> options, Func<TModel, TKey> key)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(key);
        Comparer<TKey> comparer = Comparer<TKey>.Default;
        options.CompareAscending = Compare;
        options.CompareDescending = (a, b) => Compare(b, a);
        return options;

        int Compare(TModel? x, TModel? y) =>
            x is null ? (y is null ? 0 : -1)
            : y is null ? 1
            : comparer.Compare(key(x), key(y));
    }
}
