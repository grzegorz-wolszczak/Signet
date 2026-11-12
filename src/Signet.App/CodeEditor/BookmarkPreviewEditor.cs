using System;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;

namespace Signet.App.CodeEditor;

/// <summary>
/// The preview of the Show Bookmarks popup: the whole file of the selected bookmark in a read-only editor, coloured like
/// Code Editor in the colours of the theme, with the numbers of the lines, the bookmark column (the lines that carry a
/// bookmark) and the line of the bookmark in the middle of the view.
/// </summary>
public sealed class BookmarkPreviewEditor : TextEditor
{
    private const string CodeFont = "Cascadia Code,Cascadia Mono,Consolas,Menlo,Monospace";
    private const double PreviewFontSize = 13;
    private const int MaxCenteringAttempts = 10;

    private readonly BookmarkMargin _bookmarkMargin = new();
    private IReadOnlyCollection<int> _bookmarkedLines = Array.Empty<int>();
    private SyntaxColorizer? _colorizer;

    /// <summary>Creates the editor (read-only, not focusable, so that the keys stay with the popup).</summary>
    public BookmarkPreviewEditor()
    {
        FontFamily = new FontFamily(CodeFont);
        FontSize = PreviewFontSize;
        IsReadOnly = true;
        Focusable = false;
        WordWrap = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Document = new TextDocument();
        Options.HighlightCurrentLine = true;
        EditorTheming.ShowThemedLineNumbers(this);
        _bookmarkMargin.Lines = () => _bookmarkedLines;
        TextArea.LeftMargins.Insert(1, _bookmarkMargin);

        // Ctrl + wheel changes the font size, like in the other code editors of the application that are not tabs.
        EditorZoom.Attach(this);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>
    /// Gives the editor the font of Code Editor (<see cref="Signet.Core.Misc.CodeEditorAppearance"/>); the zoom of Ctrl + wheel
    /// comes on top of its size.
    /// </summary>
    public void ApplyFont(Signet.Core.Misc.CodeEditorAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (!string.IsNullOrWhiteSpace(appearance.FontFamily))
        {
            FontFamily = new FontFamily(appearance.FontFamily);
        }

        FontWeight = FontWeightOptions.ToAvalonia(appearance.FontWeight);
        EditorZoom.SetBaseSize(this, appearance.FontSize > 0 ? appearance.FontSize : PreviewFontSize);
    }

    /// <summary>Shows a file with <paramref name="line"/> (1-based) in the middle of the view.</summary>
    /// <param name="text">The text of the file.</param>
    /// <param name="syntax">Its syntax, for the colours.</param>
    /// <param name="line">The line to bring into view; 0 = none.</param>
    /// <param name="bookmarkedLines">The lines of the file that carry a bookmark.</param>
    public void Show(string text, CodeEditorSyntax syntax, int line, IReadOnlyCollection<int> bookmarkedLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(bookmarkedLines);
        _bookmarkedLines = bookmarkedLines;
        Document.Text = text;
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Remove(_colorizer);
        }

        ResolvedTheme theme = AppTheme.Current;
        EditorTheming.Apply(this, theme);
        _colorizer = CodeEditorTextMate.CreateColorizer(syntax, extended: false, linkExists: null, theme);
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        int target = Math.Clamp(line, 1, Math.Max(1, Document.LineCount));
        TextArea.Caret.Line = target;
        TextArea.Caret.Column = 1;
        TextArea.TextView.Redraw();
        _bookmarkMargin.InvalidateVisual();

        // The view has its size only once it is laid out: the line is put in the middle then.
        CenterWhenLaidOut(target);
    }

    // Puts the line in the middle of the view as soon as the text has been laid out (the popup is shown right after this).
    private void CenterWhenLaidOut(int line)
    {
        if (TryCenterOn(line))
        {
            return;
        }

        // Tried again after each layout, a few times at most (a line that cannot be brought into view is given up).
        int attempts = 0;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (TryCenterOn(line) || ++attempts >= MaxCenteringAttempts)
            {
                LayoutUpdated -= handler;
            }
        };
        LayoutUpdated += handler;
    }

    // False while the text has not been laid out yet (the scrolling would be cut to nothing); true once the line is in place.
    private bool TryCenterOn(int line)
    {
        TextView view = TextArea.TextView;
        if (view.Bounds.Height <= 0 || line > Document.LineCount || ViewportHeight <= 0 || ExtentHeight <= 0)
        {
            return false;
        }

        // AvaloniaEdit puts the line in the middle of the view when it is not in it.
        ScrollTo(line, 1);
        double top = view.GetVisualTopByDocumentLine(line);
        return top >= VerticalOffset && top <= VerticalOffset + ViewportHeight;
    }
}
