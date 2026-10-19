using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ReportsViewModel"/> (the "Reports" dialog).</summary>
public sealed class ReportsViewModelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    private readonly Book _book = BookCreator.CreateNewBook("3.0");

    public void Dispose()
    {
        _disposables.Dispose();
        _book.Dispose();
    }

    [Fact]
    public void Constructor_PopulatesAllReportsFromCurrentBook()
    {
        // Arrange
        ReportsViewModel vm = new(_book);

        // Act & Assert
        vm.AllFiles.Should().NotBeEmpty();
        vm.HtmlFiles.Should().NotBeEmpty();
        vm.AllFiles.Should().HaveCount(_book.GetAllResources().Count);
    }

    [Fact]
    public void Refresh_RebuildsCollectionsAfterBookChanges()
    {
        // Arrange
        ReportsViewModel vm = new(_book);
        int before = vm.AllFiles.Count;

        // Act
        _book.GetFolderKeeper().AddContentFileToFolder(WriteTempCss());
        vm.Refresh();

        // Assert
        vm.AllFiles.Should().HaveCount(before + 1);
    }

    [Fact]
    public void NavigateToFile_RaisesNavigationRequestedWithOffsetZero()
    {
        // Arrange
        ReportsViewModel vm = new(_book);
        (string BookPath, int Offset)? received = null;
        vm.NavigationRequested += (path, offset) => received = (path, offset);

        // Act
        vm.NavigateToFile("OEBPS/Text/chapter.xhtml");

        // Assert
        received.Should().Be(("OEBPS/Text/chapter.xhtml", 0));
    }

    [Fact]
    public void NavigateToOffset_RaisesNavigationRequestedWithClampedNonNegativeOffset()
    {
        // Arrange
        ReportsViewModel vm = new(_book);
        (string BookPath, int Offset)? received = null;
        vm.NavigationRequested += (path, offset) => received = (path, offset);

        // Act
        vm.NavigateToOffset("styles/style.css", -5);

        // Assert
        received.Should().Be(("styles/style.css", 0));
    }

    [Fact]
    public void ExportCsvCommand_DefaultsToSelectedTabIndexWhenNoParameterGiven()
    {
        // Arrange
        ReportsViewModel vm = new(_book) { SelectedTabIndex = 3 };
        int? requestedTab = null;
        vm.ExportCsvRequested += (_, tab) => requestedTab = tab;

        // Act
        vm.ExportCsvCommand.Execute(null);

        // Assert
        requestedTab.Should().Be(3);
    }

    [Fact]
    public void BuildCsv_AllFilesTab_HasHeaderAndOneRowPerResource()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        ReportsViewModel vm = new(_book);

        // Act
        string csv = vm.BuildCsv(0);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        lines[0].Should().Be("Name,Type,Size (bytes),In Spine");
        (lines.Length - 1).Should().Be(vm.AllFiles.Count);
    }

    [Fact]
    public void BuildCsv_WordCharacterCountsTab_AppendsTotalRow()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        ReportsViewModel vm = new(_book);

        // Act
        string csv = vm.BuildCsv(8);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        lines.Last().Should().StartWith("TOTAL,");
    }

    private string WriteTempCss()
    {
        string path = _disposables.DeleteFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".css"));
        System.IO.File.WriteAllText(path, "body { color: black; }");
        return path;
    }
}
