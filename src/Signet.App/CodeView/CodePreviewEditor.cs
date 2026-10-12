using System;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;

namespace Signet.App.CodeView;

/// <summary>
/// A read-only editor showing a code sample the way Code View shows a file: highlighted in the colors of a theme, with
/// line numbers and in the given typeface — the preview of the editor font on the
/// "Fonts" page of the Preferences window.
/// </summary>
public sealed class CodePreviewEditor : TextEditor
{
    private SyntaxColorizer? _colorizer;

    /// <summary>Creates the editor (read-only, not focusable, with line numbers).</summary>
    public CodePreviewEditor()
    {
        IsReadOnly = true;
        EditorTheming.ShowThemedLineNumbers(this);
        Focusable = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Document = new TextDocument();
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>Shows <paramref name="text"/> as XHTML in the given typeface and colors.</summary>
    /// <param name="text">The sample.</param>
    /// <param name="family">The typeface; empty = the editor's default.</param>
    /// <param name="size">The size (px).</param>
    /// <param name="theme">The colors.</param>
    /// <param name="extended">Whether to use the extended highlighting of Code View.</param>
    public void Show(string text, string family, double size, ResolvedTheme theme, bool extended)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(theme);
        if (Document.Text != text)
        {
            Document.Text = text;
        }

        if (!string.IsNullOrWhiteSpace(family))
        {
            FontFamily = new FontFamily(family);
        }

        if (size > 0)
        {
            FontSize = size;
        }

        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Remove(_colorizer);
        }

        _colorizer = CodeViewTextMate.CreateColorizer(CodeViewSyntax.Html, extended, linkExists: null, theme);
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        EditorTheming.Apply(this, theme);
    }
}
