using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Like upstream, the control is registered in the default Avalonia XAML namespace, so the themes and the views use
// <TreeDataGrid> without a prefix. Signet does not reference the official TreeDataGrid package, so nothing collides.
[assembly: XmlnsDefinition("https://github.com/avaloniaui", "Signet.Controls.TreeDataGrid")]
[assembly: XmlnsDefinition("https://github.com/avaloniaui", "Signet.Controls.TreeDataGrid.Primitives")]
[assembly: InternalsVisibleTo("Signet.Controls.TreeDataGrid.Tests")]
