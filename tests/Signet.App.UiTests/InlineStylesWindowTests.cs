using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Extract Inline Styles to Classes" dialog: its tree, the minimum and "Extract".</summary>
public sealed class InlineStylesWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [AvaloniaFact]
    public void The_tree_shows_the_classes_and_extract_changes_the_book_and_closes_the_dialog()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace(
            "<p>Hello, world.</p>",
            "<p style=\"text-indent:1em\">a</p><p style=\"text-indent:1em\">b</p><p style=\"margin:0\">c</p>",
            StringComparison.Ordinal));
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);
        InlineStylesViewModel vm = new(survey, (_, _) => { }, plan =>
        {
            book.ExtractInlineStyles(plan, survey);
            return Task.FromResult(true);
        });
        Window owner = new();
        owner.Show();

        // Act
        Task shown = new InlineStylesWindow { DataContext = vm }.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        InlineStylesWindow window = owner.OwnedWindows.OfType<InlineStylesWindow>().Single();
        window.CaptureRenderedFrame();
        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "Tree");

        // Assert
        tree.Source!.Rows.Count.Should().Be(1, "the class, collapsed");
        Button extract = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExtractButton");
        extract.IsEnabled.Should().BeTrue();

        // Act
        vm.MinOccurrences = 1;
        Dispatcher.UIThread.RunJobs();

        // Assert
        tree.Source!.Rows.Count.Should().Be(2, "the class used once appears at a lower minimum");

        // Act
        vm.MinOccurrences = 2;
        Dispatcher.UIThread.RunJobs();
        extract.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        shown.IsCompleted.Should().BeTrue("the dialog closes after extracting");
        chapter.GetText().Should().Contain("<p class=\"signet-cls-1\">a</p>");
        owner.Close();
    }
}
