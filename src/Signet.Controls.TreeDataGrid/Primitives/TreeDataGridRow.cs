using System;
using System.Text;
using Avalonia.Controls.Metadata;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Controls.Selection;
using Signet.Controls.TreeDataGrid.Selection;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Signet.Controls.TreeDataGrid.Primitives
{
    [PseudoClasses(":selected")]
    public class TreeDataGridRow : TemplatedControl
    {
        private const double DragDistance = 3;
        private static readonly Point s_InvalidPoint = new(double.NegativeInfinity, double.NegativeInfinity);

        public static readonly DirectProperty<TreeDataGridRow, IColumns?> ColumnsProperty =
            AvaloniaProperty.RegisterDirect<TreeDataGridRow, IColumns?>(
                nameof(Columns),
                o => o.Columns);

        public static readonly DirectProperty<TreeDataGridRow, TreeDataGridElementFactory?> ElementFactoryProperty =
            AvaloniaProperty.RegisterDirect<TreeDataGridRow, TreeDataGridElementFactory?>(
                nameof(ElementFactory),
                o => o.ElementFactory,
                (o, v) => o.ElementFactory = v);

        public static readonly DirectProperty<TreeDataGridRow, bool> IsSelectedProperty =
            AvaloniaProperty.RegisterDirect<TreeDataGridRow, bool>(
                nameof(IsSelected),
                o => o.IsSelected);

        public static readonly DirectProperty<TreeDataGridRow, IRows?> RowsProperty =
            AvaloniaProperty.RegisterDirect<TreeDataGridRow, IRows?>(
                nameof(Rows),
                o => o.Rows);

        private IColumns? _columns;
        private TreeDataGridElementFactory? _elementFactory;
        private bool _isSelected;
        private IRows? _rows;
        private Point _mouseDownPosition = s_InvalidPoint;
        private PointerPressedEventArgs? _pressedEvent;
        private TreeDataGrid? _treeDataGrid;
        private bool _keepHorizontalScroll;

        static TreeDataGridRow()
        {
            GotFocusEvent.AddClassHandler<TreeDataGridRow>((x, e) => x.OnCellGotFocus(e));
            RequestBringIntoViewEvent.AddClassHandler<TreeDataGridRow>((x, e) => x.OnRequestBringIntoView(e));
        }

        public IColumns? Columns
        {
            get => _columns;
            private set => SetAndRaise(ColumnsProperty, ref _columns, value);
        }

        public TreeDataGridElementFactory? ElementFactory
        {
            get => _elementFactory;
            set => SetAndRaise(ElementFactoryProperty, ref _elementFactory, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            private set => SetAndRaise(IsSelectedProperty, ref _isSelected, value);
        }

        public object? Model => DataContext;

        public IRows? Rows
        {
            get => _rows;
            private set => SetAndRaise(RowsProperty, ref _rows, value);
        }

        public TreeDataGridCellsPresenter? CellsPresenter { get; private set; }
        public int RowIndex { get; private set; }

        public void Realize(
            TreeDataGridElementFactory? elementFactory,
            ITreeDataGridSelectionInteraction? selection,
            IColumns? columns,
            IRows? rows,
            int rowIndex)
        {
            ElementFactory = elementFactory;
            Columns = columns;
            Rows = rows;
            DataContext = rows?[rowIndex].Model;
            IsSelected = selection?.IsRowSelected(rowIndex) ?? false;
            RowIndex = rowIndex;
            UpdateSelection(selection);
            CellsPresenter?.Realize(rowIndex);
            _treeDataGrid?.RaiseRowPrepared(this, RowIndex);
        }

        public Control? TryGetCell(int columnIndex)
        {
            return CellsPresenter?.TryGetElement(columnIndex);
        }

        public void UpdateIndex(int index)
        {
            if (RowIndex == -1)
                throw new InvalidOperationException("Row is not realized.");

            RowIndex = index;
            CellsPresenter?.UpdateRowIndex(index);
        }

        public void Unrealize()
        {
            _treeDataGrid?.RaiseRowClearing(this, RowIndex);
            RowIndex = -1;
            DataContext = null;
            IsSelected = false;
            CellsPresenter?.Unrealize();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            _treeDataGrid = this.FindLogicalAncestorOfType<TreeDataGrid>();
            base.OnAttachedToLogicalTree(e);
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            _treeDataGrid = null;
            base.OnDetachedFromLogicalTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // The row may be realized before being parented. In this case raise the RowPrepared event here.
            if (_treeDataGrid is not null && RowIndex >= 0)
                _treeDataGrid.RaiseRowPrepared(this, RowIndex);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            CellsPresenter = e.NameScope.Find<TreeDataGridCellsPresenter>("PART_CellsPresenter");

            if (RowIndex >= 0)
                CellsPresenter?.Realize(RowIndex);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _mouseDownPosition = !e.Handled ? e.GetPosition(this) : s_InvalidPoint;
            _pressedEvent = !e.Handled ? e : null;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            var currentPoint = e.GetCurrentPoint(this);
            var delta = currentPoint.Position - _mouseDownPosition;

            var pointerSupportsDrag = currentPoint.Pointer.Type switch
            {
                PointerType.Mouse => currentPoint.Properties.IsLeftButtonPressed,
                PointerType.Pen => currentPoint.Properties.IsRightButtonPressed,
                _ => false
            };

            if (!pointerSupportsDrag ||
                e.Handled ||
                Math.Abs(delta.X) < DragDistance && Math.Abs(delta.Y) < DragDistance ||
                _mouseDownPosition == s_InvalidPoint ||
                _pressedEvent is null)
                return;

            var pressed = _pressedEvent;
            _mouseDownPosition = s_InvalidPoint;
            _pressedEvent = null;

            var presenter = Parent as TreeDataGridRowsPresenter;
            var owner = presenter?.TemplatedParent as TreeDataGrid;
            owner?.RaiseRowDragStarted(pressed);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _mouseDownPosition = s_InvalidPoint;
            _pressedEvent = null;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _mouseDownPosition = s_InvalidPoint;
            _pressedEvent = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == IsSelectedProperty)
            {
                PseudoClasses.Set(":selected", IsSelected);
            }
            
            base.OnPropertyChanged(change);
        }

        internal void UpdateSelection(ITreeDataGridSelectionInteraction? selection)
        {
            IsSelected = selection?.IsRowSelected(RowIndex) ?? false;
            CellsPresenter?.UpdateSelection(selection);
        }

        // Signet: in row selection mode, bringing a row or one of its cells into view (selection moved with the
        // keyboard, a cell focused by a click or by the selection model) scrolls only vertically: the horizontal scroll
        // offset stays where the user put it. Upstream, the ScrollViewer brought the focused cell into view, so arrow
        // keys in a horizontally scrolled grid jumped back to the first column. Tab still brings a cell into view.
        private void OnCellGotFocus(FocusChangedEventArgs e)
        {
            _keepHorizontalScroll = e.NavigationMethod != NavigationMethod.Tab && e.Source is TreeDataGridCell;
            if (_keepHorizontalScroll)
                Dispatcher.UIThread.Post(() => _keepHorizontalScroll = false);
        }

        private void OnRequestBringIntoView(RequestBringIntoViewEventArgs e)
        {
            if (_treeDataGrid?.RowSelection is null ||
                e.TargetObject is not Visual target ||
                (!ReferenceEquals(target, this) &&
                 !(_keepHorizontalScroll && target is TreeDataGridCell && target.FindAncestorOfType<TreeDataGridRow>() == this)) ||
                this.FindAncestorOfType<ScrollContentPresenter>() is not { } viewport ||
                viewport.TranslatePoint(default, this) is not { } viewportOrigin ||
                target.TransformToVisual(this) is not { } toRow)
                return;

            var targetInRow = e.TargetRect.TransformToAABB(toRow);
            e.TargetObject = this;
            e.TargetRect = new Rect(viewportOrigin.X, targetInRow.Y, viewport.Bounds.Width, targetInRow.Height);
        }

        public void UnrealizeOnItemRemoved()
        {
            _treeDataGrid?.RaiseRowClearing(this, RowIndex);
            RowIndex = -1;
            DataContext = null;
            IsSelected = false;
            CellsPresenter?.UnrealizeOnRowRemoved();
        }
    }
}
