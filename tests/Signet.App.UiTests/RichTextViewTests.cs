using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;
using Signet.Core.RichContent;

namespace Signet.App.UiTests;

/// <summary>The drawing of formatted text (<see cref="RichTextView"/>), e.g. in a tooltip.</summary>
public sealed class RichTextViewTests
{
    private static (Window Window, RichTextView View) Show(string markup)
    {
        AppTheme.Apply(Avalonia.Application.Current!, AppTheme.Current);
        RichTextView view = new() { Document = RichTextMarkup.Parse(markup) };
        Window window = new() { Content = view, Width = 500, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public void Bold_italic_and_code_fragments_are_set_as_such_and_code_takes_the_code_font_and_color()
    {
        // Act
        (Window window, RichTextView view) = Show("Sets the direction.\n**Keywords:** `ltr`, *or* `rtl`");

        // Assert
        Run[] runs = view.Children.OfType<TextBlock>().Single().Inlines!.OfType<Run>().ToArray();
        runs.Single(r => r.Text == "Keywords:").FontWeight.Should().Be(FontWeight.Bold);
        runs.Single(r => r.Text == "or").FontStyle.Should().Be(FontStyle.Italic);
        Run code = runs.Single(r => r.Text == "ltr");
        code.FontFamily.Should().Be(view.FindResource(RichTextView.CodeFontFamilyKey));
        code.Foreground.Should().BeSameAs(view.FindResource(RichTextView.CodeBrushKey), "code has the color of the theme (the accent by default)");
        view.Children.OfType<TextBlock>().Single().Inlines!.OfType<LineBreak>().Should().ContainSingle();
        window.Close();
    }

    [AvaloniaFact]
    public void A_separator_is_a_line_and_a_code_block_is_highlighted_in_the_colors_of_the_scheme()
    {
        // Act
        (Window window, RichTextView view) = Show("Before\n---\n```css\np { color: red; }\n```");

        // Assert
        view.Children.Should().HaveCount(3);
        view.Children[1].Should().BeOfType<Rectangle>();
        TextBlock code = view.Children[2].Should().BeOfType<Border>().Subject.Child.Should().BeOfType<TextBlock>().Subject;
        Run[] runs = code.Inlines!.OfType<Run>().ToArray();
        string.Concat(runs.Select(r => r.Text)).Should().Be("p { color: red; }");
        runs.Where(r => r.Foreground is not null).Should().NotBeEmpty("the selector and the property are colored as in the editor");
        window.Close();
    }
}
