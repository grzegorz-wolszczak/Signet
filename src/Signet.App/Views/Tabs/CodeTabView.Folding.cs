using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using Signet.App.CodeEditor;
using Signet.App.ViewModels.Tabs;
using Signet.Core.MainUI;

namespace Signet.App.Views.Tabs;

/// <summary>
/// Folding (<see cref="CodeFolding"/>): markers in the gutter, right of the icon column, fold a multi-line
/// element, comment or CSS block; a folded region shows <c>[…]</c> (<c>{…}</c> in CSS), and a click on it unfolds the
/// region. The regions follow the edits (recomputed after a short pause), and what is folded is kept in the tab's view
/// model while the tab is open (<see cref="CodeTabViewModel.FoldedRegions"/>).
/// </summary>
public partial class CodeTabView
{
    private static readonly TimeSpan FoldingUpdateDelay = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _foldingTimer = new() { Interval = FoldingUpdateDelay };
    private FoldingManager? _foldingManager;

    // The markers of the gutter, in place of the ones of AvaloniaEdit (which has them between the dotted line and the code).
    private readonly FoldingGutterMargin _foldingGutter = new();

    // The document the folding manager was installed for (FoldingManager does not expose it).
    private TextDocument? _foldingDocument;

    private void InitializeFolding()
    {
        _foldingTimer.Tick += (_, _) =>
        {
            _foldingTimer.Stop();
            UpdateFoldings();
        };
        Editor.TextChanged += (_, _) =>
        {
            if (_foldingManager is not null)
            {
                _foldingTimer.Stop();
                _foldingTimer.Start();
            }
        };

        Editor.AddHandler(PointerMovedEvent, OnGutterPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Editor.AddHandler(PointerExitedEvent, OnGutterPointerExited, Avalonia.Interactivity.RoutingStrategies.Direct, handledEventsToo: true);

        // A click on the [...] of a folded region unfolds it (AvaloniaEdit itself needs a double click).
        Editor.TextArea.TextView.AddHandler(PointerPressedEvent, OnFoldedRegionPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // Remembers what is folded in the view model that is being left (the view is reused for other tabs).
    private void SaveFoldedRegions(CodeTabViewModel? viewModel)
    {
        if (viewModel is null || _foldingManager is null || !ReferenceEquals(_foldingDocument, viewModel.Document))
        {
            return;
        }

        viewModel.FoldedRegions = _foldingManager.AllFoldings
            .Where(f => f.IsFolded)
            .Select(f => (f.StartOffset, f.EndOffset))
            .ToList();
    }

    // (Re)installs folding for the editor's current document — only for markup and CSS — and folds again what was
    // folded in this tab.
    private void InstallFolding()
    {
        if (_foldingManager is not null && ReferenceEquals(_foldingDocument, Editor.Document)
            && _boundViewModel is not null && Foldable(_boundViewModel.Syntax))
        {
            return;
        }

        _foldingTimer.Stop();
        if (_foldingManager is not null)
        {
            // The document may change before the data context does: keep what was folded in the tab being left.
            SaveFoldedRegions(_boundViewModel);
            FoldingManager.Uninstall(_foldingManager);
            Editor.TextArea.LeftMargins.Remove(_foldingGutter);
            _foldingGutter.Manager = null;
            _foldingManager = null;
            _foldingDocument = null;
        }

        if (Editor.Document is null || _boundViewModel is not { } vm || !ReferenceEquals(vm.Document, Editor.Document) || !Foldable(vm.Syntax))
        {
            return;
        }

        _foldingManager = FoldingManager.Install(Editor.TextArea);
        _foldingDocument = Editor.Document;
        MoveFoldingMarginIntoGutter();
        UpdateFoldings();
        HashSet<(int, int)> folded = vm.FoldedRegions.ToHashSet();
        foreach (FoldingSection section in _foldingManager.AllFoldings)
        {
            if (folded.Contains((section.StartOffset, section.EndOffset)))
            {
                section.IsFolded = true;
            }
        }
    }

    // The markers of AvaloniaEdit are taken out (the manager works without them) and the ones of the gutter go in, right of
    // the icon column and left of the dotted line that ends the gutter.
    private void MoveFoldingMarginIntoGutter()
    {
        var margins = Editor.TextArea.LeftMargins;
        foreach (FoldingMargin stock in margins.OfType<FoldingMargin>().ToList())
        {
            margins.Remove(stock);
        }

        if (margins.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault() is not { } separator)
        {
            return;
        }

        _foldingGutter.Manager = _foldingManager;
        _foldingGutter.ShowLines = _boundViewModel?.ShowFoldingLines ?? false;
        margins.Insert(margins.IndexOf(separator), _foldingGutter);
    }

    // The chevrons of the open regions show while the pointer is over the gutter, which is told by the position (the columns
    // draw themselves; the pointer over a number is not over the folding column).
    private void OnGutterPointerMoved(object? sender, PointerEventArgs e)
    {
        Point inTextView = e.GetPosition(Editor.TextArea.TextView);
        bool inGutter = IsInGutter(inTextView);
        _foldingGutter.GutterHovered = inGutter;
        _foldingGutter.PointerLine = inGutter ? BookmarkMargin.LineAt(Editor.TextArea.TextView, inTextView.Y) ?? 0 : 0;
    }

    private void OnGutterPointerExited(object? sender, PointerEventArgs e)
    {
        _foldingGutter.GutterHovered = false;
        _foldingGutter.PointerLine = 0;
    }

    private void UpdateFoldings()
    {
        if (_foldingManager is null || _boundViewModel is not { } vm || Editor.Document is null)
        {
            return;
        }

        string text = Editor.Document.Text;
        IReadOnlyList<FoldRegion> regions = vm.Syntax == CodeEditorSyntax.Css ? CodeFolding.ForCss(text) : CodeFolding.ForMarkup(text);
        _foldingManager.UpdateFoldings(regions.Select(r => new NewFolding(r.StartOffset, r.EndOffset) { Name = r.Title }), -1);
        _foldingGutter.InvalidateVisual();
    }

    private void OnFoldedRegionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_foldingManager is null || !e.GetCurrentPoint(Editor.TextArea.TextView).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TextView textView = Editor.TextArea.TextView;
        Point point = e.GetPosition(textView) + textView.ScrollOffset;
        if (textView.GetVisualLineFromVisualTop(point.Y) is not { } line)
        {
            return;
        }

        int column = line.GetVisualColumnFloor(point);
        VisualLineElement? element = line.Elements.FirstOrDefault(el => column >= el.VisualColumn && column < el.VisualColumn + el.VisualLength);
        if (element is null)
        {
            return;
        }

        int start = line.FirstDocumentLine.Offset + element.RelativeTextOffset;
        FoldingSection? section = _foldingManager.GetFoldingsAt(start)
            .FirstOrDefault(f => f.IsFolded && f.EndOffset == start + element.DocumentLength);
        if (section is not null)
        {
            section.IsFolded = false;
            e.Handled = true;
        }
    }

    private static bool Foldable(CodeEditorSyntax syntax) => syntax is CodeEditorSyntax.Html or CodeEditorSyntax.Xml or CodeEditorSyntax.Css;
}
