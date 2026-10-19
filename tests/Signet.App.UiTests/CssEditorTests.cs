using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;

namespace Signet.App.UiTests;

/// <summary>
/// The CSS field (<see cref="CssEditor"/>), the zoom of the editors of the Preferences window with Ctrl + wheel
/// (<see cref="EditorZoom"/>) with its hint (<see cref="ZoomHint"/>), and the room right of the line numbers.
/// </summary>
public sealed class CssEditorTests
{
    private static Window Show(params Control[] content)
    {
        StackPanel panel = new();
        foreach (Control control in content)
        {
            panel.Children.Add(control);
        }

        Window window = new() { Width = 600, Height = 400, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_css_text_binds_both_ways_and_is_highlighted_as_css()
    {
        // Arrange
        CssEditor editor = new() { Height = 100 };
        Window window = Show(editor);

        // Act
        editor.CssText = "margin: 0 0 0.5em 0;";

        // Assert
        editor.Document.Text.Should().Be("margin: 0 0 0.5em 0;", "the property reaches the document");

        // Act
        editor.Document.Text = "color: red;";

        // Assert
        editor.CssText.Should().Be("color: red;", "typing reaches the property");

        // Act & Assert
        editor.TextArea.TextView.LineTransformers.Should().Contain(t => t.GetType().Name.StartsWith("LineStateSyntaxColorizer"),
            "the text is highlighted");
        editor.IsReadOnly.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void The_problem_found_by_the_locator_is_underlined_and_follows_the_text()
    {
        // Arrange
        CssEditor editor = new() { Height = 100 };
        Window window = Show(editor);
        editor.ErrorLocator = text => text.IndexOf("oops", System.StringComparison.Ordinal) is var at and >= 0 ? (at, 4) : null;

        // Act
        editor.CssText = "color: red;";

        // Assert
        editor.ErrorRange.Should().BeNull("nothing is wrong");

        // Act
        editor.CssText = "color: red;\noops: 1;";

        // Assert
        editor.ErrorRange.Should().Be((12, 4));

        // Act
        editor.Document.Text = "oops";

        // Assert
        editor.ErrorRange.Should().Be((0, 4), "typing moves the underline");

        // Act
        editor.Document.Text = "fixed";

        // Assert
        editor.ErrorRange.Should().BeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_wheel_zooms_every_editor_by_the_same_amount_and_shows_the_size()
    {
        // Arrange
        EditorZoom.Delta = 0;
        ZoomHint.Enabled = true;
        CssEditor first = new() { Height = 100 };
        CssEditor second = new() { Height = 100 };
        Window window = Show(first, second);
        double before = first.FontSize;

        // Act
        window.MouseWheel(new Point(100, 50), new Vector(0, 1), RawInputModifiers.Control);
        window.MouseWheel(new Point(100, 50), new Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        // Assert
        EditorZoom.Delta.Should().Be(2);
        first.FontSize.Should().Be(before + 2);
        second.FontSize.Should().Be(before + 2, "the zoom is shared");
        AdornerLayer.GetAdornerLayer(first)!.Children.OfType<Border>().Should().ContainSingle("the hint with the size")
            .Which.Child.Should().BeOfType<TextBlock>().Which.Text.Should().Be($"{before + 2:0.#} px");

        // Act
        window.MouseWheel(new Point(100, 50), new Vector(0, 1), RawInputModifiers.None); // no Ctrl: no zoom

        // Assert
        EditorZoom.Delta.Should().Be(2);

        // Act
        EditorZoom.Delta = 0;

        // Assert
        first.FontSize.Should().Be(before);
        window.Close();
    }

    [AvaloniaFact]
    public void The_zoom_hint_is_not_shown_when_it_is_turned_off_and_goes_after_a_moment()
    {
        // Arrange
        EditorZoom.Delta = 0;
        CssEditor editor = new() { Height = 100 };
        Window window = Show(editor);
        ZoomHint.Enabled = false;
        try
        {
            // Act
            window.MouseWheel(new Point(100, 50), new Vector(0, 1), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            // Assert
            AdornerLayer.GetAdornerLayer(editor)!.Children.OfType<Border>().Should().BeEmpty("the preference is off");
            EditorZoom.Delta.Should().Be(1, "the zoom itself still works");
        }
        finally
        {
            ZoomHint.Enabled = true;
            EditorZoom.Delta = 0;
        }

        // Act
        window.MouseWheel(new Point(100, 50), new Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        // Assert
        AdornerLayer.GetAdornerLayer(editor)!.Children.OfType<Border>().Should().ContainSingle();
        EditorZoom.Delta = 0;
        window.Close();
    }

    [AvaloniaFact]
    public void The_zoom_is_limited()
    {
        // Arrange
        EditorZoom.Delta = 1000;

        // Act & Assert
        EditorZoom.Delta.Should().Be(EditorZoom.MaxDelta);
        EditorZoom.SizeFor(13).Should().Be(13 + EditorZoom.MaxDelta);
        EditorZoom.SizeFor(40).Should().Be(EditorZoom.MaxSize, "no editor goes beyond the largest size");
        EditorZoom.Delta = -1000;
        EditorZoom.Delta.Should().Be(EditorZoom.MinDelta);
        EditorZoom.SizeFor(13).Should().BeGreaterThanOrEqualTo(EditorZoom.MinSize);
        EditorZoom.Delta = 0;
    }

    [AvaloniaFact]
    public void The_line_numbers_have_room_to_the_right_of_the_numbers()
    {
        // Arrange
        TextEditor stock = new() { ShowLineNumbers = true, Text = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\nk", FontSize = 13 };
        TextEditor themed = new() { Text = stock.Text, FontSize = 13 };
        EditorTheming.ShowThemedLineNumbers(themed);
        Window window = Show(stock, themed);

        // Act
        double stockWidth = stock.TextArea.LeftMargins.OfType<LineNumberMargin>().First().Bounds.Width;
        double themedWidth = themed.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single().Bounds.Width;

        // Assert
        themedWidth.Should().BeApproximately(stockWidth + ThemedLineNumberMargin.RightPadding, 0.5);
        window.Close();
    }
}
