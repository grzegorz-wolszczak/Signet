using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests of <see cref="FileBatchReporter"/> and <see cref="SkippedFilesReport"/>: what the status bar says after an
/// operation applied file by file, and what goes to the report window.
/// </summary>
public sealed class FileBatchReporterTests
{
    private readonly Fixture _fixture = new();

    private List<SkippedFile> Skipped(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new SkippedFile(_fixture.Create<string>(), _fixture.Create<string>(), Line: 3, Column: 7))
            .ToList();

    [Fact]
    public void ShowStatus_without_skipped_files_shows_the_success_message_and_no_report()
    {
        // Arrange
        Mock<IStatusBarService> statusBar = new();
        string success = _fixture.Create<string>();
        FileBatchResult result = new(Processed: 4, Modified: true, Skipped: Array.Empty<SkippedFile>());

        // Act
        SkippedFilesReport? report = FileBatchReporter.ShowStatus(statusBar.Object, _fixture.Create<string>(), result, success);

        // Assert
        report.Should().BeNull();
        statusBar.Verify(s => s.ShowMessage(success, It.IsAny<TimeSpan>(), NotificationLevel.Info), Times.Once);
    }

    [Fact]
    public void ShowStatus_with_skipped_files_warns_with_the_counts_and_returns_the_report()
    {
        // Arrange
        Mock<IStatusBarService> statusBar = new();
        string operation = _fixture.Create<string>();
        List<SkippedFile> skipped = Skipped(2);
        FileBatchResult result = new(Processed: 5, Modified: true, Skipped: skipped);

        // Act
        SkippedFilesReport? report = FileBatchReporter.ShowStatus(statusBar.Object, operation, result, _fixture.Create<string>());

        // Assert
        report.Should().NotBeNull();
        report!.OperationName.Should().Be(operation);
        report.Processed.Should().Be(5);
        report.Skipped.Should().Equal(skipped);
        statusBar.Verify(
            s => s.ShowMessage(
                It.Is<string>(m => m.Contains(operation, StringComparison.Ordinal) && m.Contains('5') && m.Contains('2')),
                It.IsAny<TimeSpan>(),
                NotificationLevel.Warning),
            Times.Once);
    }

    [Fact]
    public void ShowStatus_when_every_file_was_skipped_warns_and_returns_the_report()
    {
        // Arrange
        Mock<IStatusBarService> statusBar = new();
        FileBatchResult result = new(Processed: 0, Modified: false, Skipped: Skipped(3));

        // Act
        SkippedFilesReport? report = FileBatchReporter.ShowStatus(
            statusBar.Object, _fixture.Create<string>(), result, _fixture.Create<string>());

        // Assert
        report.Should().NotBeNull();
        report!.Skipped.Should().HaveCount(3);
        statusBar.Verify(
            s => s.ShowMessage(It.IsAny<string>(), It.IsAny<TimeSpan>(), NotificationLevel.Warning), Times.Once);
    }

    [Fact]
    public void Report_lists_each_skipped_file_with_its_reason_and_position()
    {
        // Arrange
        List<SkippedFile> skipped = Skipped(2);
        SkippedFilesReport report = new(_fixture.Create<string>(), Processed: 1, skipped);

        // Act
        IReadOnlyList<LoadWarning> warnings = report.ToWarnings();

        // Assert
        warnings.Should().ContainSingle();
        warnings[0].Items.Select(i => (i.BookPath, i.Reason, i.Line, i.Column))
            .Should().Equal(skipped.Select(s => (s.BookPath, s.Reason, s.Line, s.Column)).Select(t => ((string?)t.BookPath, t.Reason, t.Line, t.Column)));
    }

    [Fact]
    public void Report_header_names_the_operation_and_the_counts()
    {
        // Arrange
        string operation = _fixture.Create<string>();
        SkippedFilesReport report = new(operation, Processed: 8, Skipped(2));

        // Act & Assert
        report.Header.Should().Contain(operation).And.Contain("8").And.Contain("2");
    }
}
