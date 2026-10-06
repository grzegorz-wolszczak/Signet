using System;

namespace Signet.Controls.TreeDataGrid.Selection
{
    public class TreeSelectionModelSourceResetEventArgs : EventArgs
    {
        public TreeSelectionModelSourceResetEventArgs(IndexPath parentIndex)
        {
            ParentIndex = parentIndex;
        }

        public IndexPath ParentIndex { get; }
    }
}
