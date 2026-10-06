using System.Collections.Generic;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.Controls.TreeDataGrid.Selection
{
    public interface ITreeDataGridColumnSelectionModel : ISelectionModel
    {
        new IReadOnlyList<IColumn?> SelectedItems { get; }
        new IColumn? SelectedItem { get; set; }
    }
}
