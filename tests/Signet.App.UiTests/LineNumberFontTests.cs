using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using System.Linq;

namespace Signet.App.UiTests;

/// <summary>The line numbers of an editor: their own font, and following the editor's again.</summary>
public sealed class LineNumberFontTests
{
    [AvaloniaFact]
    public void The_line_numbers_have_the_editor_font_until_they_are_given_one_of_their_own()
    {
        TextEditor editor = new() { FontFamily = new FontFamily("Courier New"), FontSize = 14, Text = "a\nb\nc" };
        EditorTheming.ShowThemedLineNumbers(editor);
        Window window = new() { Width = 400, Height = 300, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        ThemedLineNumberMargin margin = editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        double inherited = margin.Bounds.Width;

        margin.GetValue(TextBlock.FontSizeProperty).Should().Be(14, "the numbers take the size of the editor");

        EditorTheming.ApplyLineNumberFont(editor, "Arial", 28);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        margin.GetValue(TextBlock.FontSizeProperty).Should().Be(28);
        margin.GetValue(TextBlock.FontFamilyProperty).Name.Should().Be("Arial");
        editor.FontSize.Should().Be(14, "the text of the editor keeps its own size");
        margin.Bounds.Width.Should().BeGreaterThan(inherited, "bigger numbers need a wider margin");

        editor.FontSize = 20;
        EditorTheming.ApplyLineNumberFont(editor, string.Empty, 0);
        Dispatcher.UIThread.RunJobs();

        margin.GetValue(TextBlock.FontSizeProperty).Should().Be(20, "the numbers follow the editor again");
        margin.GetValue(TextBlock.FontFamilyProperty).Name.Should().Be("Courier New");
        window.Close();
    }
}
