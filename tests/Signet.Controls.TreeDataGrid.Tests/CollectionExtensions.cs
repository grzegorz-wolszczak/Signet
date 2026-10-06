using Avalonia.Collections;
using Avalonia.Diagnostics;

namespace Signet.Controls.TreeDataGrid.Tests
{
    internal static class CollectionExtensions
    {
        public static int CollectionChangedSubscriberCount<T>(this AvaloniaListDebug<T> list)
        {
            return list.GetCollectionChangedSubscribers()?.Length ?? 0;
        }
    }
}
