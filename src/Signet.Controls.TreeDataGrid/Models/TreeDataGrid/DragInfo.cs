using System.Collections.Generic;

namespace Signet.Controls.TreeDataGrid.Models
{
    /// <summary>
    /// Holds information about an automatic row drag/drop operation carried out
    /// by <see cref="Avalonia.Controls.TreeDataGrid.AutoDragDropRows"/>.
    /// </summary>
    public class DragInfo
    {
        /// <summary>
        /// Defines the (in-process) data format in an <see cref="Avalonia.Input.IDataTransfer"/>.
        /// </summary>
        /// <remarks>Avalonia 12: a typed <see cref="Avalonia.Input.DataFormat{T}"/> instead of a format name.</remarks>
        public static readonly Avalonia.Input.DataFormat<DragInfo> DataFormat =
            Avalonia.Input.DataFormat.CreateInProcessFormat<DragInfo>("TreeDataGridDragInfo");

        /// <summary>
        /// Initializes a new instance of the <see cref="DragInfo"/> class.
        /// </summary>
        /// <param name="source">The source of the drag operation/</param>
        /// <param name="indexes">The indexes being dragged.</param>
        public DragInfo(ITreeDataGridSource source, IEnumerable<IndexPath> indexes)
        {
            Source = source;
            Indexes = indexes;
        }

        /// <summary>
        /// Gets the <see cref="ITreeDataGridSource"/> that rows are being dragged from.
        /// </summary>
        public ITreeDataGridSource Source { get; }

        /// <summary>
        /// Gets or sets the model indexes of the rows being dragged.
        /// </summary>
        public IEnumerable<IndexPath> Indexes { get; }
    }
}
