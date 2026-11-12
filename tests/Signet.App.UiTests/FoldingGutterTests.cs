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
/// (a folded region keeps its chevron), and a click that folds and unfolds.
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
