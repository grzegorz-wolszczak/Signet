using Avalonia.Media;

namespace Signet.Controls.TreeDataGrid.Models
{
    public interface ICellOptions
    {
        /// <summary>
        /// Gets the gesture(s) that will cause the cell to enter edit mode.
        /// </summary>
        BeginEditGestures BeginEditGestures { get; }
    }
}
