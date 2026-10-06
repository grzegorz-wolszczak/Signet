using System.Collections;

namespace Signet.Controls.TreeDataGrid.Selection
{
    public interface ITreeDataGridSelection
    {
        IEnumerable? Source { get; set; }
    }
}
