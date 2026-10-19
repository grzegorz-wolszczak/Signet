using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of reporting the progress of an operation applied file by file and of stopping it (<see cref="FileBatchRun"/>).</summary>
public sealed class FileBatchRunTests
{
    private sealed class Recorder : IProgress<FileBatchProgress>
    {
        private readonly Action<FileBatchProgress>? _onReport;

        public Recorder(Action<FileBatchProgress>? onReport = null) => _onReport = onReport;

        public List<FileBatchProgress> Reports { get; } = new();

        public void Report(FileBatchProgress value)
        {
            Reports.Add(value);
            _onReport?.Invoke(value);
        }
    }

    private static Book BookWithFiles(int extra)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        for (int i = 0; i < extra; i++)
        {
            book.CreateEmptyHtmlFile();
        }

        return book;
    }

    [Fact]
    public void Each_file_is_reported_before_it_is_processed_with_how_many_are_done_and_the_total()
    {
        // Arrange
        using Book book = BookWithFiles(extra: 3);
        List<HtmlResource> files = book.GetHtmlResources().ToList();
        Recorder recorder = new();

        // Act
        FileBatchResult result = book.MendAllHtml(run: new FileBatchRun(recorder, CancellationToken.None));

        // Assert
        result.Processed.Should().Be(files.Count);
        recorder.Reports.Select(r => r.Done).Should().Equal(Enumerable.Range(0, files.Count));
        recorder.Reports.Should().OnlyContain(r => r.Total == files.Count);
        recorder.Reports.Select(r => r.BookPath).Should().Equal(files.Select(f => f.BookPath));
    }

    [Fact]
    public void A_token_cancelled_before_the_start_stops_the_operation_before_the_first_file()
    {
        // Arrange
        using Book book = BookWithFiles(extra: 2);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Recorder recorder = new();

        // Act & Assert
        book.Invoking(b => b.AddSoftHyphens(new FileBatchRun(recorder, cancelled.Token))).Should().Throw<OperationCanceledException>();
        recorder.Reports.Should().BeEmpty();
    }

    [Fact]
    public void Cancelling_in_the_middle_stops_the_operation_between_two_files()
    {
        // Arrange
        using Book book = BookWithFiles(extra: 4);
        using CancellationTokenSource source = new();
        Recorder recorder = new(report =>
        {
            if (report.Done == 2)
            {
                source.Cancel();
            }
        });

        // Act & Assert
        book.Invoking(b => b.MendAllHtml(run: new FileBatchRun(recorder, source.Token))).Should().Throw<OperationCanceledException>();
        recorder.Reports.Select(r => r.Done).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Without_a_run_the_operation_goes_through_all_files_as_before()
    {
        // Arrange
        using Book book = BookWithFiles(extra: 2);

        // Act
        FileBatchResult result = book.RemoveSoftHyphens();

        // Assert
        result.Processed.Should().Be(book.GetHtmlResources().Count);
        result.HasSkipped.Should().BeFalse();
    }
}
