using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
/// The line along the block the caret is in (Preferences › Code Editor): drawn in the text, at the column where the block is
/// indented, from the line after its first line to the one before its last; the blocks around get it weaker.
/// </summary>
public sealed class BlockLineTests : IDisposable
{
    // Lines: 1 <body>, 2 <div>, 3 <div>, 4 <p>a</p>, 5 <p>b</p>, 6 </div>, 7 </div>, 8 </body> — the inner div is lines 3 to 6.
    private const string Text = "<body>\n  <div>\n    <div>\n      <p>a</p>\n      <p>b</p>\n    </div>\n  </div>\n</body>";

    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Window Window, CodeTabViewModel Vm, SettingsStore Settings, TextEditor Editor) Show()
    {
        string root = _disposables.Add(new TempDir("signet-uitests-")).Path;
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(
            settings, System.IO.Path.Combine(root, "hunspell_dictionaries"), System.IO.Path.Combine(root, "user_dictionaries"));
        string path = System.IO.Path.Combine(root, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, Text);
        HtmlResource html = new(root, path);
        html.InitialLoad();

        CodeTabViewModel vm = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        Window window = new() { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, settings, window.GetVisualDescendants().OfType<TextEditor>().Single());
    }

    // What the renderer of the block line draws now: the lines (x, top, bottom) with the pen they are drawn with.
    private static List<(double X, double Top, double Bottom, IPen Pen)> LinesDrawn(TextEditor editor)
    {
        TextView view = editor.TextArea.TextView;
        IBackgroundRenderer renderer = view.BackgroundRenderers.First(r => r.GetType().Name == "BlockLineRenderer");
        DrawingGroup drawing = new();
        using (DrawingContext context = drawing.Open())
        {
            renderer.Draw(view, context);
        }

        return drawing.Children.OfType<GeometryDrawing>().Where(d => d.Pen is not null)
            .Select(d => (d.Geometry!.Bounds.X, d.Geometry.Bounds.Top, d.Geometry.Bounds.Bottom, d.Pen!)).ToList();
    }

    private static void TurnOn(CodeTabViewModel vm, SettingsStore settings, TextEditor editor, int caretLine, int thickness = 1)
    {
        settings.CodeEditorShowBlockLine = true;
        settings.CodeEditorBlockLineThickness = thickness;
        vm.NotifyInvisibleCharactersSettingChanged();
        editor.TextArea.Caret.Line = caretLine;
        editor.TextArea.Caret.Column = 8;
        Dispatcher.UIThread.RunJobs();
    }

    private static double RowTop(TextEditor editor, int line) =>
        editor.TextArea.TextView.GetVisualTopByDocumentLine(line) - editor.TextArea.TextView.VerticalOffset;

    [AvaloniaFact]
    public void Nothing_is_drawn_while_the_setting_is_off()
    {
        // Arrange
        (Window window, _, _, TextEditor editor) = Show();
        editor.TextArea.Caret.Line = 4;
        Dispatcher.UIThread.RunJobs();

        // Act & Assert
        LinesDrawn(editor).Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void The_line_runs_inside_the_block_of_the_caret_with_the_blocks_around_it_weaker()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, SettingsStore settings, TextEditor editor) = Show();

        // Act
        TurnOn(vm, settings, editor, caretLine: 4);
        List<(double X, double Top, double Bottom, IPen Pen)> lines = LinesDrawn(editor).OrderByDescending(l => l.X).ToList();

        // Assert
        lines.Should().HaveCount(3, "the inner div, the div around it and the body");
        (double X, double Top, double Bottom, IPen Pen) inner = lines[0];
        (double X, double Top, double Bottom, IPen Pen) outer = lines[1];
        (double X, double Top, double Bottom, IPen Pen) body = lines[2];
        inner.Top.Should().BeApproximately(RowTop(editor, 4), 1, "from the line after the first line of the block");
        inner.Bottom.Should().BeApproximately(RowTop(editor, 6), 1, "to the line before its last line");
        outer.Top.Should().BeApproximately(RowTop(editor, 3), 1);
        outer.Bottom.Should().BeApproximately(RowTop(editor, 7), 1);
        body.Top.Should().BeApproximately(RowTop(editor, 2), 1);
        body.Bottom.Should().BeApproximately(RowTop(editor, 8), 1);
        inner.X.Should().BeGreaterThan(outer.X, "an inner block is indented further");
        outer.X.Should().BeGreaterThan(body.X);
        ((ISolidColorBrush)inner.Pen.Brush!).Color.A.Should().BeGreaterThan(((ISolidColorBrush)outer.Pen.Brush!).Color.A, "the block of the caret is the stronger one");
        window.Close();
    }

    [AvaloniaFact]
    public void The_line_follows_the_caret_to_another_block_and_has_the_thickness_of_the_setting()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, SettingsStore settings, TextEditor editor) = Show();
        TurnOn(vm, settings, editor, caretLine: 4, thickness: 3);

        // Act
        editor.TextArea.Caret.Line = 7; // in the outer div only
        Dispatcher.UIThread.RunJobs();
        List<(double X, double Top, double Bottom, IPen Pen)> lines = LinesDrawn(editor);

        // Assert
        lines.Should().HaveCount(2, "the outer div and the body have the caret now, the inner div does not");
        lines.Should().OnlyContain(l => l.Pen.Thickness == 3);
        lines.OrderByDescending(l => l.X).First().Top.Should().BeApproximately(RowTop(editor, 3), 1, "the outer div, which starts on line 2");
        window.Close();
    }

    [AvaloniaFact]
    public void A_block_without_a_line_between_its_first_and_last_has_no_line()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, SettingsStore settings, TextEditor editor) = Show();

        // Act
        TurnOn(vm, settings, editor, caretLine: 1); // <body> is the only block that has the caret: lines 1 to 8

        // Assert
        LinesDrawn(editor).Should().ContainSingle();

        // Act
        settings.CodeEditorShowBlockLine = false;
        vm.NotifyInvisibleCharactersSettingChanged();
        Dispatcher.UIThread.RunJobs();

        // Assert
        LinesDrawn(editor).Should().BeEmpty("the setting was switched off again");
        window.Close();
    }
}
