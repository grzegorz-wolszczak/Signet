using Signet.Controls.TreeDataGrid.Models;

namespace Signet.Controls.TreeDataGrid.Selection
{
    public class TreeDataGridColumnSelectionModel : SelectionModel<IColumn>,
        ITreeDataGridColumnSelectionModel
    {
        public TreeDataGridColumnSelectionModel(IColumns columns)
            : base(columns)
        {
        }
    }
}
