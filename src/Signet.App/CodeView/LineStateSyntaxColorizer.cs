using System;
using System.Collections.Generic;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Signet.Core.Misc;

namespace Signet.App.CodeView;

/// <summary>
/// Code View syntax highlighting with line-by-line highlighters (basic —
/// <see cref="XhtmlHighlighter"/>, <see cref="CssHighlighter"/> — or extended —
/// <see cref="ExtendedXhtmlHighlighter"/>, <see cref="ExtendedCssHighlighter"/>): the end state
/// of every line is remembered, and a line is colorized synchronously when it is drawn (only
/// visible lines), without a background thread.
/// </summary>
/// <remarks>
/// After an edit, the states from the changed line downwards are invalidated. When the end state
/// of the edited line changed (e.g. a comment was opened) or the change spanned several lines,
/// the view is redrawn so that the following lines are re-highlighted until the state stabilizes.
/// </remarks>
/// <typeparam name="TState">Type of the highlighter's line state.</typeparam>
internal sealed class LineStateSyntaxColorizer<TState> : SyntaxColorizer
    where TState : IEquatable<TState>
{
    private readonly ILineSyntaxHighlighter<TState> _highlighter;
    private readonly List<TState> _endStates = new(); // _endStates[i] = end state of line i+1
    private readonly List<SyntaxSpan> _spans = new();
    private TextDocument? _document;
    private bool _redrawPosted;

    public LineStateSyntaxColorizer(ILineSyntaxHighlighter<TState> highlighter, CodeViewAppearance appearance)
        : base(appearance)
    {
        _highlighter = highlighter ?? throw new ArgumentNullException(nameof(highlighter));
    }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        TextDocument document = CurrentContext.Document;
        TextView = CurrentContext.TextView;
        EnsureDocument(document);

        TState startState = StartStateOf(line.LineNumber);
        _spans.Clear();
        TState endState = _highlighter.HighlightLine(document.GetText(line), startState, _spans);
        StoreEndState(line.LineNumber, endState);
        ApplySpans(line, _spans);
    }

    private void EnsureDocument(TextDocument document)
    {
        if (ReferenceEquals(document, _document))
        {
            return;
        }

        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }

        _document = document;
        _document.Changed += OnDocumentChanged;
        _endStates.Clear();
    }

    // State at the start of a line (1-based) — computes missing end states of previous lines.
    private TState StartStateOf(int lineNumber)
    {
        if (lineNumber == 1)
        {
            return _highlighter.InitialState;
        }

        while (_endStates.Count < lineNumber - 1)
        {
            int next = _endStates.Count + 1;
            TState previous = next == 1 ? _highlighter.InitialState : _endStates[next - 2];
            _endStates.Add(_highlighter.HighlightLine(_document!.GetText(_document.GetLineByNumber(next)), previous, null));
        }

        return _endStates[lineNumber - 2];
    }

    private void StoreEndState(int lineNumber, TState endState)
    {
        if (_endStates.Count >= lineNumber)
        {
            _endStates[lineNumber - 1] = endState;
        }
        else if (_endStates.Count == lineNumber - 1)
        {
            _endStates.Add(endState);
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        int changedLine = _document.GetLineByOffset(Math.Min(e.Offset, _document.TextLength)).LineNumber;
        bool hadOldEndState = _endStates.Count >= changedLine;
        TState? oldEndState = hadOldEndState ? _endStates[changedLine - 1] : default;

        // Invalidate states from the changed line downwards.
        int keep = changedLine - 1;
        if (_endStates.Count > keep)
        {
            _endStates.RemoveRange(keep, _endStates.Count - keep);
        }

        bool multiLine = ContainsLineBreak(e.InsertedText) || ContainsLineBreak(e.RemovedText);
        if (!multiLine)
        {
            if (!hadOldEndState)
            {
                return; // lines below were not computed yet — they will be computed when drawn
            }

            TState newEndState = _highlighter.HighlightLine(
                _document.GetText(_document.GetLineByNumber(changedLine)), StartStateOf(changedLine), null);
            StoreEndState(changedLine, newEndState);
            if (newEndState.Equals(oldEndState))
            {
                return; // AvaloniaEdit redraws the edited line by itself
            }
        }

        // The state carried over to the following lines — redraw the view (after the current change completes).
        if (!_redrawPosted && TextView is not null)
        {
            _redrawPosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                _redrawPosted = false;
                TextView?.Redraw();
            });
        }
    }

    private static bool ContainsLineBreak(ITextSource? text) =>
        text is not null && text.TextLength > 0 && text.IndexOfAny(['\n', '\r'], 0, text.TextLength) >= 0;
}
