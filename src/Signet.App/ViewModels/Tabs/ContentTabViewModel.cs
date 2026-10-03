using System;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Base view model of a document tab. It is at the same time a Dock.Avalonia <see cref="Document"/>,
/// so it goes straight into the <c>DocumentDock</c> area.
/// </summary>
/// <remarks>
/// The concrete editors/viewers (Code View, CSS, Image/Font/AV/SVG/PDF/XML)
/// inherit from this class and override <see cref="Save"/>, <see cref="Reload"/>, the undo/redo hooks,
/// <see cref="ZoomFactor"/> and <see cref="IsWellFormed"/>. The placeholder implementation is
/// <see cref="PlaceholderContentTabViewModel"/>.
/// </remarks>
public abstract class ContentTabViewModel : Document, IDeferredContentPresentation
{
    private double _zoomFactor = 1.0;

    /// <summary>
    /// Disables Dock's deferred content for document tabs. When docks were re-parented,
    /// <c>DeferredContentControl</c> materialized the content through the dispatcher queue, which
    /// caused re-entrancy in the views' <c>DataContextChanged</c> handlers (among others
    /// <c>CodeTabView</c>: a <c>NullReferenceException</c> when the <c>TextEditor.Document</c> binding
    /// was not yet resolved). With a handful to a dozen open files there is no gain
    /// from deferred materialization, and a whole class of this bug disappears.
    /// </summary>
    bool IDeferredContentPresentation.DeferContentPresentation => false;

    /// <summary>Initializes the tab for an entry of <see cref="TabManagerModel"/>.</summary>
    protected ContentTabViewModel(OpenTab tab)
    {
        Tab = tab ?? throw new ArgumentNullException(nameof(tab));
        Id = tab.Resource.Identifier;
        Title = tab.Caption;
        CanFloat = false;
        CanPin = false;
    }

    /// <summary>The tab model entry that this tab represents.</summary>
    public OpenTab Tab { get; }

    /// <summary>The resource displayed in the tab.</summary>
    public Resource Resource => Tab.Resource;

    /// <summary>Tab kind.</summary>
    public ContentTabKind Kind => Tab.Kind;

    /// <summary>Whether the tab offers an undo operation (not by default).</summary>
    public virtual bool CanUndo => false;

    /// <summary>Whether the tab offers a redo operation (not by default).</summary>
    public virtual bool CanRedo => false;

    /// <summary>Zoom factor of the content.</summary>
    public double ZoomFactor
    {
        get => _zoomFactor;
        set => SetProperty(ref _zoomFactor, value <= 0 ? 1.0 : value);
    }

    /// <summary>
    /// XML well-formedness of the tab content: <c>true</c>/<c>false</c>, or <c>null</c> when not applicable.
    /// <c>null</c> by default.
    /// </summary>
    public virtual bool? IsWellFormed => null;

    /// <summary>Undoes the last change (does nothing by default).</summary>
    public virtual void Undo()
    {
    }

    /// <summary>Redoes an undone change (does nothing by default).</summary>
    public virtual void Redo()
    {
    }

    /// <summary>
    /// Silently flushes the tab's changes into the in-memory resource.
    /// Called when switching and closing the tab. Does nothing by default (the placeholder does not edit).
    /// </summary>
    public virtual void Save()
    {
    }

    /// <summary>Reloads the content from the resource.</summary>
    public virtual void Reload()
    {
    }

    /// <summary>Refreshes the tab caption after the resource's name/location changes.</summary>
    internal virtual void RefreshCaption() => Title = Tab.Caption;
}
