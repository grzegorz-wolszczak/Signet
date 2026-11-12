using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AwesomeAssertions;
using Signet.App.CodeEditor;
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
/// The folding markers in the gutter: chevrons on the first line of a region that show while the pointer is over the gutter
/// (a folded region keeps its chevron), the line along the region under the pointer, and a click that folds and unfolds.
/// </summary>
public sealed class FoldingGutterTests : IDisposable
{
    // Lines: 1 <body>, 2 <div>, 3 <p>, 4 </div>, 5 <ul>, 6 <li>, 7 </ul>, 8 </body> — regions start on lines 1, 2 and 5.
    private const string Text = "<body>\n<div>\n<p>a</p>\n</div>\n<ul>\n<li>x</li>\n</ul>\n</body>";

    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Window Window, CodeTabViewModel Vm, SettingsStore Settings, TextEditor Editor, FoldingGutterMargin Margin) Show()
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
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        return (window, vm, settings, editor, editor.TextArea.LeftMargins.OfType<FoldingGutterMargin>().Single());
    }

    // The tops of the chevrons the column draws now (the strokes it records into a drawing context).
    private static double[] ChevronsIn(FoldingGutterMargin margin)
    {
        DrawingGroup drawing = new();
        using (DrawingContext context = drawing.Open())
        {
            margin.Render(context);
        }

        return drawing.Children.OfType<GeometryDrawing>().Where(d => d.Pen is not null).Select(d => d.Geometry!.Bounds.Y).Order().ToArray();
    }

    private static double RowTop(TextEditor editor, int line) =>
        editor.TextArea.TextView.VisualLines.First(l => l.FirstDocumentLine.LineNumber == line).VisualTop - editor.TextArea.TextView.VerticalOffset;

    [AvaloniaFact]
    public void The_chevrons_of_open_regions_show_only_while_the_pointer_is_over_the_gutter()
    {
        // Arrange
        (Window window, _, _, TextEditor editor, FoldingGutterMargin margin) = Show();

        // Assert
        ChevronsIn(margin).Should().BeEmpty("no region is folded and the pointer is not over the gutter");

        // Act
        margin.GutterHovered = true;

        // Assert
        double[] chevrons = ChevronsIn(margin);
        chevrons.Should().HaveCount(3, "a region starts on each of three lines");
        chevrons[0].Should().BeInRange(RowTop(editor, 1), RowTop(editor, 2));
        chevrons[1].Should().BeInRange(RowTop(editor, 2), RowTop(editor, 3));
        chevrons[2].Should().BeInRange(RowTop(editor, 5), RowTop(editor, 6));

        // Act
        margin.GutterHovered = false;

        // Assert
        ChevronsIn(margin).Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void A_folded_region_keeps_its_chevron_whether_or_not_the_gutter_is_hovered()
    {
        // Arrange
        (Window window, _, _, TextEditor editor, FoldingGutterMargin margin) = Show();

        // Act
        margin.ToggleAt(5).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        // Assert
        ChevronsIn(margin).Should().ContainSingle().Which.Should().BeInRange(RowTop(editor, 5), RowTop(editor, 5) + 30);
        margin.Manager!.AllFoldings.Count(f => f.IsFolded).Should().Be(1);
        window.Close();
    }

    [AvaloniaFact]
    public void Toggling_a_line_folds_the_region_and_unfolds_it_again_and_a_line_without_one_does_nothing()
    {
        // Arrange
        (Window window, _, _, _, FoldingGutterMargin margin) = Show();

        // Act & Assert
        margin.HasRegionAt(2).Should().BeTrue();
        margin.HasRegionAt(3).Should().BeFalse();
        margin.ToggleAt(3).Should().BeFalse();
        margin.ToggleAt(2).Should().BeTrue();
        margin.Manager!.AllFoldings.Count(f => f.IsFolded).Should().Be(1);
        margin.ToggleAt(2).Should().BeTrue();
        margin.Manager.AllFoldings.Count(f => f.IsFolded).Should().Be(0);
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_the_column_folds_the_region_of_the_line()
    {
        // Arrange
        (Window window, _, _, TextEditor editor, FoldingGutterMargin margin) = Show();
        Point inWindow = margin.TranslatePoint(new Point(margin.Bounds.Width / 2, RowTop(editor, 5) + 8), window)!.Value;
        PointerPressedEventArgs args = new(
            margin, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true), window, inWindow, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        };

        // Act
        margin.RaiseEvent(args);

        // Assert
        margin.Manager!.AllFoldings.Where(f => f.IsFolded).Select(f => editor.Document.GetLineByOffset(f.StartOffset).LineNumber)
            .Should().Equal(5);
        window.Close();
    }

    // The vertical lines the column draws (not the chevrons, which are wider): their top and bottom.
    private static (double Top, double Bottom)[] LinesIn(FoldingGutterMargin margin)
    {
        DrawingGroup drawing = new();
        using (DrawingContext context = drawing.Open())
        {
            margin.Render(context);
        }

        return drawing.Children.OfType<GeometryDrawing>().Where(d => d.Pen is not null)
            .Select(d => d.Geometry!.Bounds).Where(r => r.Width < 2).Select(r => (r.Top, r.Bottom)).ToArray();
    }

    [AvaloniaFact]
    public void A_line_joins_the_start_and_the_end_of_the_region_under_the_pointer_when_the_setting_is_on()
    {
        // Arrange
        (Window window, _, _, TextEditor editor, FoldingGutterMargin margin) = Show();
        margin.GutterHovered = true;
        margin.PointerLine = 3; // inside the regions of lines 1 and 2: the inner one, of the <div>, lines 2 to 4

        // Assert
        LinesIn(margin).Should().BeEmpty("the setting is off");

        // Act
        margin.ShowLines = true;

        // Assert
        (double top, double bottom) = LinesIn(margin).Should().ContainSingle().Subject;
        top.Should().BeInRange(RowTop(editor, 2), RowTop(editor, 3), "from the middle of line 2");
        bottom.Should().BeInRange(RowTop(editor, 4), RowTop(editor, 5), "to the middle of line 4");
        ChevronsIn(margin).Length.Should().Be(4, "the three chevrons of the starts and the line");

        // Act
        margin.GutterHovered = false;

        // Assert
        LinesIn(margin).Should().BeEmpty("the pointer left the gutter");
        window.Close();
    }

    [AvaloniaFact]
    public void The_line_goes_on_to_the_edge_when_the_end_of_the_region_is_not_in_view()
    {
        // Arrange
        (Window window, _, _, TextEditor editor, FoldingGutterMargin margin) = Show();
        margin.GutterHovered = true;
        margin.ShowLines = true;
        margin.PointerLine = 1;
        editor.Height = RowTop(editor, 3);
        editor.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // Act
        (double Top, double Bottom)[] lines = LinesIn(margin);

        // Assert
        lines.Should().ContainSingle().Which.Top.Should().BeLessThan(RowTop(editor, 2), "it starts on line 1, the first row");
        window.Close();
    }

    [AvaloniaFact]
    public void The_setting_of_the_lines_reaches_an_open_editor()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, SettingsStore settings, _, FoldingGutterMargin margin) = Show();
        margin.ShowLines.Should().BeFalse("off by default");

        // Act
        settings.CodeEditorShowFoldingLines = true;
        vm.NotifyInvisibleCharactersSettingChanged();
        Dispatcher.UIThread.RunJobs();

        // Assert
        margin.ShowLines.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void The_stock_markers_of_the_editor_are_gone_from_the_margins()
    {
        // Arrange & Act
        (Window window, _, _, TextEditor editor, _) = Show();

        // Assert
        editor.TextArea.LeftMargins.OfType<AvaloniaEdit.Folding.FoldingMargin>().Should().BeEmpty();
        window.Close();
    }
}
