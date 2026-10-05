using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Ctrl+End in Code View must bring the end of the document into view with a single press, also when long
/// paragraphs are wrapped (AvaloniaEdit estimates the height of lines it has not built yet as one line each).
/// </summary>
public sealed class CodeTabViewCtrlEndTests
{
    private static string LongParagraphs()
    {
        StringBuilder text = new("<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<body>\n");
        string sentence = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 40));
        for (int i = 0; i < 300; i++)
        {
            text.Append("<p>").Append(sentence).Append("</p>\n");
        }

        return text.Append("</body>\n</html>").ToString();
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_single_Ctrl_End_shows_the_end_of_a_document_with_wrapped_paragraphs()
    {
        using TempDir temp = new();
        var html = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        html.SetText(LongParagraphs());
        OpenTab tab = new TabManagerModel().OpenResource(html);
        SettingsStore settings = new(temp.Combine("settings.json"));
        settings.CodeViewWordWrap = true;
        SpellChecker spellChecker = new(settings, temp.Combine("hunspell"), temp.Combine("user"));
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 700, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Settle(window);
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.WordWrap.Should().BeTrue();
        editor.TextArea.Focus();

        window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
        Settle(window);

        editor.CaretOffset.Should().Be(editor.Document.TextLength);
        TextView textView = editor.TextArea.TextView;
        double caretBottom = textView.GetVisualPosition(
            new TextViewPosition(editor.Document.GetLocation(editor.CaretOffset)), VisualYPosition.LineBottom).Y;
        caretBottom.Should().BeLessThanOrEqualTo(textView.VerticalOffset + textView.Bounds.Height + 1, "the caret line is visible");
        caretBottom.Should().BeGreaterThan(textView.VerticalOffset, "the caret line is visible");
        window.Close();
    }
}
