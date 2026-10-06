using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Controls.Selection;
using Signet.Controls.TreeDataGrid.Selection;

namespace Signet.Controls.TreeDataGrid.Primitives
{
    internal interface ITreeDataGridCell
    {
        int ColumnIndex { get; }

        void Realize(
            TreeDataGridElementFactory factory,
            ITreeDataGridSelectionInteraction? selection,
            ICell model,
            int columnIndex,
            int rowIndex);

        void Unrealize();
    }
}
