using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Signet.Controls.TreeDataGrid
{
    /// <summary>
    /// Provides data for the <see cref="TreeDataGrid.RowDragStarted"/> event.
    /// </summary>
    public class TreeDataGridRowDragStartedEventArgs : RoutedEventArgs
    {
        public TreeDataGridRowDragStartedEventArgs(IEnumerable<object> models)
            : base(TreeDataGrid.RowDragStartedEvent)
        {
            Models = models;
        }

        public DragDropEffects AllowedEffects { get; set; }
        public IEnumerable<object> Models { get; }
    }
}
