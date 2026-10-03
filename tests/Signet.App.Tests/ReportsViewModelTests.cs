using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ReportsViewModel"/> (the "Reports" dialog).</summary>
public sealed class ReportsViewModelTests : IDisposable
{
    private readonly Book _book = BookCreator.CreateNewBook("3.0");

    public void Dispose() => _book.Dispose();

    [Fact]
    public void Constructor_PopulatesAllReportsFromCurrentBook()
    {
        ReportsViewModel vm = new(_book);

        vm.AllFiles.Should().NotBeEmpty();
        vm.HtmlFiles.Should().NotBeEmpty();
        vm.AllFiles.Should().HaveCount(_book.GetAllResources().Count);
    }

    [Fact]
    public void Refresh_RebuildsCollectionsAfterBookChanges()
    {
        ReportsViewModel vm = new(_book);
        int before = vm.AllFiles.Count;

        _book.GetFolderKeeper().AddContentFileToFolder(WriteTempCss());
        vm.Refresh();

        vm.AllFiles.Should().HaveCount(before + 1);
    }

    [Fact]
    public void NavigateToFile_RaisesNavigationRequestedWithOffsetZero()
    {
        ReportsViewModel vm = new(_book);
        (string BookPath, int Offset)? received = null;
        vm.NavigationRequested += (path, offset) => received = (path, offset);

        vm.NavigateToFile("OEBPS/Text/chapter.xhtml");

        received.Should().Be(("OEBPS/Text/chapter.xhtml", 0));
    }

    [Fact]
    public void NavigateToOffset_RaisesNavigationRequestedWithClampedNonNegativeOffset()
    {
        ReportsViewModel vm = new(_book);
        (string BookPath, int Offset)? received = null;
        vm.NavigationRequested += (path, offset) => received = (path, offset);

        vm.NavigateToOffset("styles/style.css", -5);

        received.Should().Be(("styles/style.css", 0));
    }

    [Fact]
    public void ExportCsvCommand_DefaultsToSelectedTabIndexWhenNoParameterGiven()
    {
        ReportsViewModel vm = new(_book) { SelectedTabIndex = 3 };
        int? requestedTab = null;
        vm.ExportCsvRequested += (_, tab) => requestedTab = tab;

        vm.ExportCsvCommand.Execute(null);

        requestedTab.Should().Be(3);
    }

    [Fact]
    public void BuildCsv_AllFilesTab_HasHeaderAndOneRowPerResource()
    {
        using UiCultureScope culture = new("en");
        ReportsViewModel vm = new(_book);

        string csv = vm.BuildCsv(0);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("Name,Type,Size (bytes),In Spine");
        (lines.Length - 1).Should().Be(vm.AllFiles.Count);
    }

    [Fact]
    public void BuildCsv_WordCharacterCountsTab_AppendsTotalRow()
    {
        using UiCultureScope culture = new("en");
        ReportsViewModel vm = new(_book);

        string csv = vm.BuildCsv(8);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Last().Should().StartWith("TOTAL,");
    }

    private static string WriteTempCss()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".css");
        System.IO.File.WriteAllText(path, "body { color: black; }");
        return path;
    }
}
