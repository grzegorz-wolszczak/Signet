using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="EpubCheckValidator"/>: what EPUBCheck says about a book, as validation results.</summary>
public sealed class EpubCheckValidatorTests
{
    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo s_polish = CultureInfo.GetCultureInfo("pl");

    private static Book Load(TempDir temp, string folder)
    {
        string epub = EpubBuilder.BuildInto(folder, temp);
        return new ImportEpub(epub).GetBook();
    }

    private sealed class Recorder : IProgress<ValidationProgress>
    {
        public List<ValidationProgress> Reports { get; } = [];

        public void Report(ValidationProgress value) => Reports.Add(value);
    }

    [Fact]
    public void Validate_OfACleanBook_ReportsNothing()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Media);

        // Act
        IReadOnlyList<ValidationResult> results = EpubCheckValidator.Validate(book, culture: s_english);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_FindsTheProblemOfTheTextThatIsOnlyInMemory_WithTheMessageIdAsTheCode()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        html.SetText("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>not closed</body></html>");

        // Act
        IReadOnlyList<ValidationResult> results = EpubCheckValidator.Validate(book, culture: s_english);

        // Assert
        ValidationResult result = results.Should().ContainSingle(r => r.BookPath == html.BookPath && r.Severity >= ValidationSeverity.Error).Subject;
        result.Code.Should().MatchRegex("^[A-Z]{3}-\\d{3}$");
        result.Line.Should().BeGreaterThan(0);
        result.Message.Should().NotBeNullOrWhiteSpace().And.NotContain("\n");
    }

    [Fact]
    public void Validate_SpeaksTheLanguageAsked()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        html.SetText("<html><body><p>not closed</body>");

        // Act
        string english = EpubCheckValidator.Validate(book, culture: s_english).First(r => r.BookPath == html.BookPath).Message;
        string polish = EpubCheckValidator.Validate(book, culture: s_polish).First(r => r.BookPath == html.BookPath).Message;

        // Assert
        polish.Should().NotBe(english);
        english.Should().Contain("Error");
    }

    [Fact]
    public void Validate_WithASkippedMessageId_LeavesThatMessageOut_AndKeepsTheRest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        html.SetText("<html><body><p>not closed</body>");
        IReadOnlyList<ValidationResult> all = EpubCheckValidator.Validate(book, culture: s_english);
        string skipped = all.First(r => r.BookPath == html.BookPath).Code;

        // Act
        IReadOnlyList<ValidationResult> results = EpubCheckValidator.Validate(book, [skipped, "Validation_DeadLink"], s_english);

        // Assert
        all.Should().Contain(r => r.Code == skipped);
        results.Should().NotContain(r => r.Code == skipped);
        results.Count.Should().Be(all.Count(r => r.Code != skipped));
    }

    [Fact]
    public void Validate_ReportsUsageOnlyWhenAsked()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3WithNcx);

        // Act
        IReadOnlyList<ValidationResult> without = EpubCheckValidator.Validate(book, culture: s_english);
        IReadOnlyList<ValidationResult> with = EpubCheckValidator.Validate(book, culture: s_english, includeUsage: true);

        // Assert
        without.Should().NotContain(r => r.Severity == ValidationSeverity.Usage);
        with.Count.Should().BeGreaterThanOrEqualTo(without.Count);
        with.Where(r => r.Severity != ValidationSeverity.Usage).Should().BeEquivalentTo(without);
    }

    [Fact]
    public void Validate_TellsHowManyFilesItHasSeen()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        Recorder recorder = new();

        // Act
        EpubCheckValidator.Validate(book, culture: s_english, run: new ValidationRun(recorder));

        // Assert
        recorder.Reports.Should().NotBeEmpty().And.OnlyContain(r => r.Stage == ValidationStage.EpubCheck);
        recorder.Reports.Last().Done.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(recorder.Reports.Last().Total);
    }

    [Fact]
    public void Validate_StopsWhenCancelled()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        // Act & Assert
        Action act = () => EpubCheckValidator.Validate(book, culture: s_english, run: new ValidationRun(Cancellation: cancellation.Token));
        act.Should().Throw<OperationCanceledException>();
    }
}
