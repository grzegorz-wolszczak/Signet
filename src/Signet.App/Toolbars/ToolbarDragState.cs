using System;
using Avalonia.Input;

namespace Signet.App.Toolbars;

/// <summary>
/// The toolbar that is being dragged by its grip (UI thread only). The toolbar areas read it while a drag is
/// over them, so that they can show the drop indicator — and the empty areas can show a place to drop on.
/// </summary>
public static class ToolbarDragState
{
    /// <summary>Format of the drag data that carries the dragged toolbar's id.</summary>
    public static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("signet.toolbar-id");

    /// <summary>The dragged toolbar, or <see langword="null"/> when no toolbar is being dragged.</summary>
    public static ToolbarId? Current { get; private set; }

    /// <summary>Raised when a drag starts or ends.</summary>
    public static event EventHandler? Changed;

    /// <summary>Marks <paramref name="toolbar"/> as being dragged.</summary>
    public static void Begin(ToolbarId toolbar)
    {
        Current = toolbar;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Marks the end of the drag (dropped or cancelled).</summary>
    public static void End()
    {
        Current = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
