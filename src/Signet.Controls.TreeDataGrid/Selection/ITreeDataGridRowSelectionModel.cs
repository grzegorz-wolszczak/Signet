using System;
using System.Collections.Generic;

namespace Signet.Controls.TreeDataGrid.Selection
{
    public interface ITreeDataGridRowSelectionModel : ITreeSelectionModel, ITreeDataGridSelection
    {
    }

    public interface ITreeDataGridRowSelectionModel<T> : ITreeDataGridRowSelectionModel
    {
        new T? SelectedItem { get; }
        new IReadOnlyList<T?> SelectedItems { get; }
        new event EventHandler<TreeSelectionModelSelectionChangedEventArgs<T>>? SelectionChanged;
    }
}
