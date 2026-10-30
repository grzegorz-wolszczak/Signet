using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// A stripe of panel buttons (<see cref="PanelStripeViewModel"/>). A button dragged further than a few pixels moves its
/// panel: dropped into a group of this stripe or of the other one, the panel goes to the place of that group, at the
/// position the line shows (<see cref="PanelStripesViewModel.Drop"/>); the click of the button is not made then.
/// </summary>
public partial class PanelStripeView : UserControl
{
    private const double DragThreshold = 6;
    private const string WiredClass = "stripeWired";
    private const string DraggingClass = "dragging";

    private PanelStripeViewModel? _bound;
    private Button? _pressed;
    private Point _pressPoint;
    private bool _dragging;

    /// <summary>Creates the stripe.</summary>
    public PanelStripeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
    }

    private void Bind()
    {
        if (_bound is not null)
        {
            _bound.Top.CollectionChanged -= OnGroupsChanged;
            _bound.Middle.CollectionChanged -= OnGroupsChanged;
        }

        _bound = DataContext as PanelStripeViewModel;
        if (_bound is not null)
        {
            _bound.Top.CollectionChanged += OnGroupsChanged;
            _bound.Middle.CollectionChanged += OnGroupsChanged;
            // The line between the stripe and the window's content.
            Frame.BorderThickness = _bound.Side == PanelStripeSide.Left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
            ResizeGrip.HorizontalAlignment = _bound.Side == PanelStripeSide.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            ((PopupFlyoutBase)MoreButton.Flyout!).Placement = _bound.Side == PanelStripeSide.Left ? PlacementMode.RightEdgeAlignedTop : PlacementMode.LeftEdgeAlignedTop;
        }

        UpdateSeparator();
    }

    // The grip moves with the edge it sits on, so each delta is from where the edge is now; outwards widens the stripe.
    private void OnResizeDelta(object? sender, VectorEventArgs e) =>
        _bound?.Owner.ResizeBy(_bound.Side == PanelStripeSide.Left ? e.Vector.X : -e.Vector.X);

    private void OnResizeCompleted(object? sender, VectorEventArgs e) => _bound?.Owner.EndResize();

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSeparator();

    private void UpdateSeparator() =>
        GroupSeparator.IsVisible = _bound is { Top.Count: > 0, Middle.Count: > 0 };

    // Every button once: the title of its menu (the name of the panel, as every context menu has) and the dragging.
    private void OnButtonLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Classes.Contains(WiredClass))
        {
            return;
        }

        button.Classes.Add(WiredClass);
        if (button.ContextMenu is { } menu)
        {
            MenuTitle.AddTo(menu, () => (button.DataContext as PanelStripeButtonViewModel)?.Title ?? string.Empty);
        }

        button.AddHandler(PointerPressedEvent, OnButtonPressed, RoutingStrategies.Tunnel);
        button.AddHandler(PointerMovedEvent, OnButtonMoved, RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, OnButtonReleased, RoutingStrategies.Tunnel);
        button.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Tunnel);
    }

    private void OnButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button button && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is { } top)
        {
            _pressed = button;
            _pressPoint = e.GetPosition(top);
        }
    }

    private void OnButtonMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        Point point = e.GetPosition(top);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _pressPoint.X) < DragThreshold && Math.Abs(point.Y - _pressPoint.Y) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_pressed);
            foreach (PanelStripeView stripe in StripesOf(top))
            {
                stripe.Classes.Add(DraggingClass);
            }
        }

        ShowDropTarget(top, point);
        e.Handled = true;
    }

    private void OnButtonReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            _pressed = null;
            return;
        }

        // Dropped: the panel moves, and the release does not click the button.
        if (TopLevel.GetTopLevel(this) is { } top && _pressed?.DataContext is PanelStripeButtonViewModel dragged)
        {
            Point point = e.GetPosition(top);
            foreach (PanelStripeView stripe in StripesOf(top))
            {
                if (stripe.TryFindDropTarget(top, point, dragged, out PanelPlace place, out int index, out _))
                {
                    stripe._bound!.Owner.Drop(dragged.Id, place, index);
                    break;
                }
            }
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        bool was = _dragging;
        _dragging = false;
        _pressed = null;
        if (was && TopLevel.GetTopLevel(this) is { } top)
        {
            foreach (PanelStripeView stripe in StripesOf(top))
            {
                stripe.Classes.Remove(DraggingClass);
                stripe.DropIndicator.IsVisible = false;
            }
        }
    }

    private void ShowDropTarget(TopLevel top, Point point)
    {
        if (_pressed?.DataContext is not PanelStripeButtonViewModel dragged)
        {
            return;
        }

        foreach (PanelStripeView stripe in StripesOf(top))
        {
            bool found = stripe.TryFindDropTarget(top, point, dragged, out _, out _, out double y);
            stripe.DropIndicator.IsVisible = found;
            if (found)
            {
                Canvas.SetLeft(stripe.DropIndicator, 4);
                Canvas.SetTop(stripe.DropIndicator, y - 1);
                stripe.DropIndicator.Width = Math.Max(0, stripe.Bounds.Width - 8);
            }
        }
    }

    private static IEnumerable<PanelStripeView> StripesOf(TopLevel top) =>
        top.GetVisualDescendants().OfType<PanelStripeView>().Where(s => s._bound is not null);

    /// <summary>
    /// Where a button dropped at <paramref name="point"/> (in <paramref name="top"/>) goes on this stripe: the place of the
    /// group under it and the position among the group's other buttons, with the height of the line that shows it.
    /// The top group takes everything above the second one; the empty space between the second group and the foot is
    /// shared by the two at its middle.
    /// </summary>
    internal bool TryFindDropTarget(TopLevel top, Point point, PanelStripeButtonViewModel dragged, out PanelPlace place, out int index, out double y)
    {
        place = default;
        index = 0;
        y = 0;
        if (_bound is null || top.TranslatePoint(point, this) is not { } local
            || local.X < 0 || local.X > Bounds.Width || local.Y < 0 || local.Y > Bounds.Height)
        {
            return false;
        }

        double middleTop = TopOf(MiddleGroup);
        double middleBottom = middleTop + MiddleGroup.Bounds.Height;
        double bottomTop = TopOf(BottomGroup);
        int group = local.Y < middleTop ? 0 : local.Y < (middleBottom + bottomTop) / 2 ? 1 : 2;
        ItemsControl items = group switch
        {
            0 => TopGroup,
            1 => MiddleGroup,
            _ => BottomGroup,
        };

        place = _bound.PlaceOfGroup(group);
        y = TopOf(items);
        for (int i = 0; i < items.ItemCount; i++)
        {
            if (items.ContainerFromIndex(i) is not Control container || ReferenceEquals(container.DataContext, dragged))
            {
                continue;
            }

            double containerTop = TopOf(container);
            if (local.Y < containerTop + container.Bounds.Height / 2)
            {
                y = containerTop;
                return true;
            }

            index++;
            y = containerTop + container.Bounds.Height;
        }

        return true;
    }

    private double TopOf(Visual visual) => visual.TranslatePoint(default, this)?.Y ?? 0;
}
