using System;
using Avalonia.Controls.Primitives;
using Signet.Controls.TreeDataGrid.Primitives;

namespace Signet.Controls.TreeDataGrid
{
    public class TreeDataGridRowEventArgs
    {
        public TreeDataGridRowEventArgs(TreeDataGridRow row, int rowIndex)
        {
            Row = row;
            RowIndex = rowIndex;
        }

        internal TreeDataGridRowEventArgs()
        {
            Row = null!;
        }

        public TreeDataGridRow Row { get; private set; }
        public int RowIndex { get; private set; }

        internal void Update(TreeDataGridRow? row, int rowIndex)
        {
            if (row is object && Row is object)
                throw new NotSupportedException("Nested TreeDataGrid row prepared/clearing detected.");

            Row = row!;
            RowIndex = rowIndex;
        }
    }
}
