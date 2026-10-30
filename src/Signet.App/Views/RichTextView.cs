using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;
using Signet.Core.RichContent;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// Draws a <see cref="RichText"/>: paragraphs (bold, italic and code fragments), separators and blocks of code
/// highlighted in the colors of the editor's color scheme. The application's data template for <see cref="RichText"/>
/// uses it, so a tooltip whose content is a <see cref="RichText"/> shows it formatted, while a plain string tooltip stays
/// as it was. The text takes the font of its place (the tooltip font of the area); code takes
/// <see cref="CodeFontFamilyKey"/> and the color <see cref="CodeBrushKey"/> (theme key <c>ui.tooltip.code</c>).
/// </summary>
public sealed class RichTextView : StackPanel
{
    /// <summary>Application resource of the font of code (fragments and blocks).</summary>
    public const string CodeFontFamilyKey = "SignetRichTextCodeFontFamily";

    /// <summary>Application resource of the color of code fragments.</summary>
    public const string CodeBrushKey = "SignetRichTextCodeBrush";

    /// <summary>The document shown.</summary>
    public static readonly StyledProperty<RichText?> DocumentProperty =
        AvaloniaProperty.Register<RichTextView, RichText?>(nameof(Document));

    static RichTextView()
    {
        DocumentProperty.Changed.AddClassHandler<RichTextView>((view, _) => view.Rebuild());
    }

    /// <summary>Creates the view.</summary>
    public RichTextView() => Spacing = 6;

    /// <summary>The document shown.</summary>
    public RichText? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Document is null)
        {
            return;
        }

        foreach (RichBlock block in Document.Blocks)
        {
            Children.Add(block switch
            {
                RichParagraph paragraph => Paragraph(paragraph),
                RichCodeBlock code => CodeBlock(code),
                _ => Separator(),
            });
        }
    }

    private TextBlock Paragraph(RichParagraph paragraph)
    {
        TextBlock text = new() { TextWrapping = TextWrapping.Wrap };
        foreach (RichSpan span in paragraph.Spans)
        {
            string[] lines = span.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    text.Inlines!.Add(new LineBreak());
                }

                if (lines[i].Length > 0)
                {
                    text.Inlines!.Add(Run(lines[i], span.Kind));
                }
            }
        }

        return text;
    }

    private Run Run(string text, RichSpanKind kind)
    {
        Run run = new(text);
        switch (kind)
        {
            case RichSpanKind.Bold:
                run.FontWeight = FontWeight.Bold;
                break;
            case RichSpanKind.Italic:
                run.FontStyle = FontStyle.Italic;
                break;
            case RichSpanKind.Code:
                run.Bind(TextElement.FontFamilyProperty, this.GetResourceObservable(CodeFontFamilyKey));
                run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(CodeBrushKey));
                break;
        }

        return run;
    }

    // A block of code on a slightly darker background, highlighted as the Code Editor would (the colors of the scheme).
    private Border CodeBlock(RichCodeBlock code)
    {
        TextBlock text = new() { TextWrapping = TextWrapping.NoWrap };
        text.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable(CodeFontFamilyKey));
        // The marks of a theme sample («key|text») are not code: guillemets are shown as single ones.
        string escaped = code.Code.Replace("«", "‹", StringComparison.Ordinal).Replace("»", "›", StringComparison.Ordinal);
        ThemeSample sample = ThemeSample.Parse(escaped, SyntaxOf(code.Language));
        string source = sample.Text;
        int offset = 0;
        foreach (KeyRange range in sample.SyntaxRanges.OrderBy(r => r.Offset))
        {
            if (range.Offset < offset)
            {
                continue;
            }

            AddPlain(text, source[offset..range.Offset]);
            TextAttributes look = AppTheme.Current.Attributes(range.Key);
            Run run = new(source.Substring(range.Offset, range.Length))
            {
                FontWeight = look.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = look.Italic ? FontStyle.Italic : FontStyle.Normal,
            };
            if (look.Foreground is { } color)
            {
                run.Foreground = new SolidColorBrush(ResolvedTheme.ToColor(color));
            }

            text.Inlines!.Add(run);
            offset = range.End;
        }

        AddPlain(text, source[offset..]);
        Border border = new()
        {
            Child = text,
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable(FluentResourceKeys.SystemControlBackgroundBaseLowBrush));
        return border;
    }

    private static void AddPlain(TextBlock text, string plain)
    {
        string[] lines = plain.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                text.Inlines!.Add(new LineBreak());
            }

            if (lines[i].Length > 0)
            {
                text.Inlines!.Add(new Run(lines[i]));
            }
        }
    }

    private Rectangle Separator()
    {
        Rectangle line = new() { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2) };
        line.Bind(Shape.FillProperty, this.GetResourceObservable(FluentResourceKeys.SystemControlForegroundBaseMediumLowBrush));
        return line;
    }

    private static CodeEditorSyntax SyntaxOf(string language) => language.ToLowerInvariant() switch
    {
        "css" => CodeEditorSyntax.Css,
        "html" or "xhtml" => CodeEditorSyntax.Html,
        "xml" or "opf" or "ncx" or "svg" => CodeEditorSyntax.Xml,
        _ => CodeEditorSyntax.PlainText,
    };
}
