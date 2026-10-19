using System;
using System.Collections.Generic;
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
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.UiTests;

/// <summary>The "Change Footnote Placement" dialog: its list, the filter, the file of the global notes and "Change".</summary>
public sealed class FootnotePlacementWindowTests
{
    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    [AvaloniaFact]
    public void The_list_shows_the_footnotes_and_the_filter_and_Change_work()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
            + "<head><title>t</title></head>\n<body>\n<p>One.</p>\n<p>Two.</p>\n<p>Three.</p>\n</body>\n</html>");
        FootnoteEditor.Add(book, html, After(html, "One"), "Local note.", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Two"), "Global note.", FootnotePlacement.Global).Should().Be(FootnoteError.None);
        IReadOnlyList<PlacementItem>? applied = null;
        FootnotePlacementViewModel vm = new(
            FootnoteEditor.PlanPlacement(book), book.GetHtmlResourcesExcludingNav(), FootnoteEditor.FindGlobalFile(book), (_, _) => { },
            (items, _) =>
            {
                applied = items;
                return Task.FromResult<PlacementOutcome?>(new PlacementOutcome(items.Count, 0));
            });
        Window owner = new();

        // Act
        owner.Show();
        Task shown = FootnotePlacementWindow.ShowAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnotePlacementWindow window = owner.OwnedWindows.OfType<FootnotePlacementWindow>().Single();
        window.CaptureRenderedFrame();
        TreeDataGrid list = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "List");
        Button apply = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyButton");
        ComboBox files = window.GetVisualDescendants().OfType<ComboBox>().Single(b => b.Name == "GlobalFileBox");
        CheckBox showGlobal = window.GetVisualDescendants().OfType<CheckBox>().Single(b => b.Name == "ShowGlobalBox");

        // Assert
        list.Source!.Rows.Count.Should().Be(2);
        apply.IsEffectivelyEnabled.Should().BeFalse("nothing is checked");
        files.IsEnabled.Should().BeFalse("no footnote becomes global");
        files.SelectedItem.Should().BeSameAs(vm.GlobalFiles[0]);

        // Act
        vm.Rows[0].IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // Assert
        apply.IsEffectivelyEnabled.Should().BeTrue();
        files.IsEnabled.Should().BeTrue("a local footnote becomes global");

        // Act
        showGlobal.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        list.Source!.Rows.Count.Should().Be(1, "the global footnotes are hidden");

        // Act
        apply.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        applied.Should().ContainSingle().Which.Placement.Should().Be(FootnotePlacement.Local);
        shown.IsCompleted.Should().BeTrue("the dialog closes after the change");
        owner.Close();
    }
}
