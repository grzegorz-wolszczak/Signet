using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Count All" report in the Find &amp; Replace panel: scrolling the list and closing it.</summary>
public sealed class FindReplaceReportViewTests
{
    private const int FileCount = 30;

    [AvaloniaFact]
    public void Long_report_list_is_scrollable_instead_of_being_clipped()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        (Window window, FindReplaceView view, _) = ShowPanelWithCountReport(book, temp);

        Expander expander = view.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "ReportExpander");
        expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        ScrollViewer scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "ReportScroll");
        scroll.Viewport.Height.Should().BeLessThanOrEqualTo(180);
        scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height, "30 rows do not fit in 180 px");

        scroll.Offset = scroll.Offset.WithY(scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        scroll.Offset.Y.Should().BeGreaterThan(0, "the list can be scrolled to the end");
        window.Close();
    }

    [AvaloniaFact]
    public void Close_button_hides_the_report()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        (Window window, FindReplaceView view, FindReplaceViewModel vm) = ShowPanelWithCountReport(book, temp);

        Button close = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseReportButton");
        close.IsEffectivelyVisible.Should().BeTrue();

        close.Command.Should().BeSameAs(vm.CloseReportCommand);
        close.Command!.Execute(close.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        vm.HasReport.Should().BeFalse();
        close.IsEffectivelyVisible.Should().BeFalse();
        view.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "ReportExpander")
            .IsEffectivelyVisible.Should().BeFalse();
        window.Close();
    }

    private static (Window Window, FindReplaceView View, FindReplaceViewModel Vm) ShowPanelWithCountReport(
        Book book, TempDir temp)
    {
        TextResource[] files = Enumerable.Range(0, FileCount)
            .Select(_ =>
            {
                HtmlResource html = book.CreateEmptyHtmlFile();
                html.SetText("<html><body><p>cat cat</p></body></html>");
                return (TextResource)html;
            })
            .ToArray();

        var host = new Mock<IMultiFileSearchHost>();
        host.SetupGet(h => h.HasBook).Returns(true);
        host.Setup(h => h.ResolveLookWhere(It.IsAny<LookWhere>())).Returns(files);
        FindReplaceViewModel vm = new(
            new SettingsStore(Path.Combine(temp.Path, "settings.json")),
            new StatusBarService(),
            () => (CodeTabViewModel?)null,
            host.Object)
        {
            LookWhereIndex = (int)LookWhere.AllHtmlFiles,
            FindText = "cat",
        };
        vm.Count().Should().Be(2 * FileCount);
        vm.ReportRows.Should().HaveCount(FileCount);

        var view = new FindReplaceView { DataContext = vm };
        Window window = new() { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return (window, view, vm);
    }
}
