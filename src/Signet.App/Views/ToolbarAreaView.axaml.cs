using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Signet.App.Toolbars;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// View of one toolbar area (<see cref="ToolbarAreaViewModel"/>): its rows of toolbars, and the drop target for a toolbar
/// dragged by its grip. While a toolbar is over the area, <see cref="ToolbarDropResolver"/> turns the pointer position
/// into "into this row before that toolbar" or "into a new row here", and a line shows where it would land.
/// An area without a visible toolbar takes no space, except while a toolbar is dragged — then it shows a place to drop on.
/// </summary>
public partial class ToolbarAreaView : UserControl
{
    private ToolbarAreaViewModel? _bound;

    /// <summary>Initializes the view.</summary>
    public ToolbarAreaView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ToolbarDragState.Changed += OnDragStateChanged;
        RefreshVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ToolbarDragState.Changed -= OnDragStateChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnAreaPropertyChanged;
        }

        _bound = DataContext as ToolbarAreaViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnAreaPropertyChanged;
        }

        RefreshVisibility();
    }

    private void OnAreaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarAreaViewModel.HasVisibleToolbars))
        {
            RefreshVisibility();
        }
    }

    private void OnDragStateChanged(object? sender, EventArgs e)
    {
        RefreshVisibility();
        if (ToolbarDragState.Current is null)
        {
            Indicator.IsVisible = false;
        }
    }

    private void RefreshVisibility()
    {
        bool dragging = ToolbarDragState.Current is not null;
        bool hasToolbars = _bound?.HasVisibleToolbars ?? false;
        IsVisible = hasToolbars || dragging;
        Placeholder.IsVisible = dragging && !hasToolbars;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (ToolbarDragState.Current is null)
        {
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        // On an empty area the placeholder itself marks the target.
        if (!ResolveTarget(e, out ToolbarDropTarget target) || Placeholder.IsVisible)
        {
            Indicator.IsVisible = false;
            return;
        }

        Indicator.Margin = new Thickness(target.Indicator.X, target.Indicator.Y, 0, 0);
        Indicator.Width = target.Indicator.Width;
        Indicator.Height = target.Indicator.Height;
        Indicator.IsVisible = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => Indicator.IsVisible = false;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Indicator.IsVisible = false;
        if (ToolbarDragState.Current is not { } dragged || !ResolveTarget(e, out ToolbarDropTarget target))
        {
            return;
        }

        e.Handled = true;

        // Posted: the move rebuilds the rows (including the dragged toolbar's view) — let the drag finish first.
        ToolbarAreaViewModel area = _bound!;
        Dispatcher.UIThread.Post(() => area.Drop(dragged, target));
    }

    /// <summary>Where a toolbar dropped at <paramref name="point"/> (in the coordinates of this view) would land.</summary>
    public ToolbarDropTarget ResolveAt(Point point) =>
        ToolbarDropResolver.Resolve(CollectRows(), _bound?.Rows.Count ?? 0, new Rect(Bounds.Size), point);

    private bool ResolveTarget(DragEventArgs e, out ToolbarDropTarget target)
    {
        target = default;
        if (_bound is null)
        {
            return false;
        }

        target = ResolveAt(e.GetPosition(this));
        return true;
    }

    // The shown rows and their toolbars with bounds in the coordinates of this view. Rows and toolbars that are hidden
    // take no space, so they are skipped.
    private List<ToolbarRowSlot> CollectRows()
    {
        List<ToolbarRowSlot> rows = new();
        if (RowsHost.ItemsPanelRoot is not { } rowsPanel)
        {
            return rows;
        }

        for (int i = 0; i < rowsPanel.Children.Count; i++)
        {
            Control rowContainer = rowsPanel.Children[i];
            if (rowContainer.Bounds.Height < 1)
            {
                continue;
            }

            List<ToolbarSlot> slots = new();
            if (rowContainer is ContentPresenter { Child: ItemsControl { ItemsPanelRoot: { } stripsPanel } })
            {
                foreach (Control strip in stripsPanel.Children)
                {
                    if (strip.Bounds.Width >= 1 && strip.DataContext is ToolbarViewModel toolbar)
                    {
                        slots.Add(new ToolbarSlot(toolbar.Id, BoundsInThis(strip)));
                    }
                }
            }

            rows.Add(new ToolbarRowSlot(i, BoundsInThis(rowContainer), slots));
        }

        return rows;
    }

    private Rect BoundsInThis(Control control) =>
        new(control.TranslatePoint(default, this) ?? default, control.Bounds.Size);
}
