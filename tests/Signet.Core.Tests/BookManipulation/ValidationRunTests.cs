using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of reporting where the whole-book validation is and of stopping it (<see cref="ValidationRun"/>).</summary>
public sealed class ValidationRunTests
{
    private sealed class Recorder : IProgress<ValidationProgress>
    {
        private readonly Action<ValidationProgress>? _onReport;

        public Recorder(Action<ValidationProgress>? onReport = null) => _onReport = onReport;

        public List<ValidationProgress> Reports { get; } = new();

        public void Report(ValidationProgress value)
        {
            Reports.Add(value);
            _onReport?.Invoke(value);
        }
    }

    [Fact]
    public void The_files_are_reported_one_by_one_and_then_each_further_check()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        book.CreateEmptyHtmlFile();
        int files = book.GetHtmlResources().Count;
        Recorder recorder = new();

        // Act
        BookValidator.ValidateCurrentBook(book, null, new ValidationRun(recorder, CancellationToken.None));

        // Assert
        recorder.Reports.Where(r => r.Stage == ValidationStage.WellFormed).Select(r => (r.Done, r.Total))
            .Should().Equal(Enumerable.Range(0, files).Select(i => (i, files)));
        recorder.Reports.Select(r => r.Stage).Distinct().Should().Equal(Enum.GetValues<ValidationStage>());
    }

    [Fact]
    public void A_token_cancelled_before_the_start_stops_the_validation()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        // Act & Assert
        book.Invoking(b => BookValidator.ValidateCurrentBook(b, null, new ValidationRun(null, cancelled.Token)))
            .Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Cancelling_in_the_middle_stops_the_validation_before_the_next_check()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using CancellationTokenSource source = new();
        Recorder recorder = new(report =>
        {
            if (report.Stage == ValidationStage.Links)
            {
                source.Cancel();
            }
        });

        // Act & Assert
        book.Invoking(b => BookValidator.ValidateCurrentBook(b, null, new ValidationRun(recorder, source.Token)))
            .Should().Throw<OperationCanceledException>();
        recorder.Reports.Select(r => r.Stage).Should().NotContain(ValidationStage.Fonts);
    }
}
