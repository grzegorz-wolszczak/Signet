using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using Signet.Core.Parsers;

namespace Signet.App.UiTests;

/// <summary>The color preview of the Code Editor (<see cref="ColorPreviewController"/>): which colors it finds and how it replaces one.</summary>
public sealed class ColorPreviewControllerTests
{
    private const string Css = "p { color: #ff0000; background: rgb(0 128 0) }\n#abc { margin: 0 }\n";

    private static (Window Window, TextEditor Editor, ColorPreviewController Controller) Show(string text, ColorPreviewKind kind)
    {
        TextEditor editor = new() { Text = text };
        Window window = new() { Width = 600, Height = 300, Content = editor };
        window.Show();
        ColorPreviewController controller = new(editor) { Kind = kind, Enabled = true };
        controller.RefreshNow();
        Dispatcher.UIThread.RunJobs();
        return (window, editor, controller);
    }

    [AvaloniaFact]
    public void The_colors_of_a_stylesheet_are_found_and_the_one_under_an_offset_is_returned()
    {
        // Arrange
        (Window window, _, ColorPreviewController controller) = Show(Css, ColorPreviewKind.Css);

        // Assert
        controller.Matches.Select(m => Css.Substring(m.Start, m.Length)).Should().Equal("#ff0000", "rgb(0 128 0)");
        int inside = Css.IndexOf("#ff0000", System.StringComparison.Ordinal) + 3;
        controller.MatchAt(inside)!.Color.Should().Be(new CssColor(255, 0, 0, 255));
        controller.MatchAt(Css.IndexOf("#abc", System.StringComparison.Ordinal) + 1).Should().BeNull("an id selector is not a color");
        controller.MatchAt(Css.IndexOf("#ff0000", System.StringComparison.Ordinal) + "#ff0000".Length).Should().BeNull("the end is outside");
        window.Close();
    }

    [AvaloniaFact]
    public void The_colors_of_an_html_file_are_found_in_its_style_elements_and_attributes()
    {
        // Arrange
        const string html = "<html><head><style>p { color: red }</style></head><body><p style=\"background: #00f\">x</p></body></html>";

        // Act
        (Window window, _, ColorPreviewController controller) = Show(html, ColorPreviewKind.Html);

        // Assert
        controller.Matches.Select(m => html.Substring(m.Start, m.Length)).Should().Equal("red", "#00f");
        window.Close();
    }

    [AvaloniaFact]
    public void Nothing_is_found_when_the_preview_is_off_or_the_file_is_not_css_or_html()
    {
        // Arrange
        (Window window, _, ColorPreviewController controller) = Show(Css, ColorPreviewKind.Css);

        // Act
        controller.Enabled = false;
        controller.RefreshNow();

        // Assert
        controller.Matches.Should().BeEmpty();
        controller.MatchAt(Css.IndexOf("#ff0000", System.StringComparison.Ordinal) + 1).Should().BeNull();

        // Act
        controller.Enabled = true;
        controller.Kind = ColorPreviewKind.None;
        controller.RefreshNow();

        // Assert
        controller.Matches.Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void An_edit_makes_the_found_colors_stale_until_they_are_looked_for_again()
    {
        // Arrange
        (Window window, TextEditor editor, ColorPreviewController controller) = Show(Css, ColorPreviewKind.Css);
        controller.IsStale.Should().BeFalse();

        // Act
        editor.Document.Insert(0, " ");

        // Assert
        controller.IsStale.Should().BeTrue();
        controller.MatchAt(Css.IndexOf("#ff0000", System.StringComparison.Ordinal) + 2).Should().BeNull("the offsets are old");

        // Act
        controller.RefreshNow();

        // Assert
        controller.IsStale.Should().BeFalse();
        controller.Matches.Should().HaveCount(2);
        window.Close();
    }

    [AvaloniaFact]
    public void A_color_is_replaced_in_the_notation_it_had_as_one_undo_step()
    {
        // Arrange
        (Window window, TextEditor editor, ColorPreviewController controller) = Show(Css, ColorPreviewKind.Css);
        CssColorMatch match = controller.Matches[1];

        // Act
        controller.ApplyColor(match, new CssColor(0, 0, 255, 255));

        // Assert
        editor.Text.Should().Contain("background: rgb(0 0 255) }").And.Contain("#ff0000");
        controller.IsStale.Should().BeFalse("the colors were looked for again after the replacement");
        controller.Matches.Select(m => editor.Text.Substring(m.Start, m.Length)).Should().Equal("#ff0000", "rgb(0 0 255)");

        // Act
        editor.Undo();

        // Assert
        editor.Text.Should().Be(Css);
        window.Close();
    }

    [AvaloniaFact]
    public void A_very_large_file_is_not_scanned()
    {
        // Arrange
        string big = "p { color: #fff }\n" + new string(' ', ColorPreviewController.MaxScannedLength);

        // Act
        (Window window, _, ColorPreviewController controller) = Show(big, ColorPreviewKind.Css);

        // Assert
        controller.Matches.Should().BeEmpty();
        window.Close();
    }
}
