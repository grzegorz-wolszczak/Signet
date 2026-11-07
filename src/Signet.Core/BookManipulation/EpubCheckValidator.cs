using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks a book with the EPUBCheck port (<c>Signet.EpubCheck.Core</c>): the book is given to the checker as it is now, unsaved
/// changes included (<see cref="BookEpubSource"/>), and what the checker reports becomes <see cref="ValidationResult"/>s whose
/// <see cref="ValidationResult.Code"/> is the EPUBCheck message id (<c>OPF-003</c>) and whose text is in the language asked for.
/// </summary>
public static partial class EpubCheckValidator
{
    /// <summary>The name the publication as a whole has for the checker; a message about it has no book path.</summary>
    private const string PublicationName = "book.epub";

    /// <summary>Checks the book.</summary>
    /// <param name="book">The book.</param>
    /// <param name="skippedCodes">
    /// The rule codes that are switched off. Only the ones that are EPUBCheck message ids matter here; they make the checker
    /// treat the message as suppressed (the same as a <c>SUPPRESSED</c> line of a custom messages file).
    /// </param>
    /// <param name="culture">The language of the messages (English or Polish; any other gets English); the current UI language by default.</param>
    /// <param name="includeUsage">Whether the <see cref="ValidationSeverity.Usage"/> messages (information about the features the book uses) are reported.</param>
    /// <param name="run">Reports how far the check is and can stop it.</param>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public static IReadOnlyList<ValidationResult> Validate(
        Book book,
        IReadOnlyCollection<string>? skippedCodes = null,
        CultureInfo? culture = null,
        bool includeUsage = false,
        ValidationRun run = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        run.Cancellation.ThrowIfCancellationRequested();
        using BookEpubSource source = BookEpubSource.Create(book);
        BookValidationReport report = new(book, culture ?? CultureInfo.CurrentUICulture, Suppressed(skippedCodes), includeUsage, source.Entries.Count, run);
        report.Initialize();
        OCFChecker.Check(source, EpubVersion.Unknown, report, new EpubLocation(PublicationName));
        run.Cancellation.ThrowIfCancellationRequested();
        return report.Results;
    }

    private static Dictionary<MessageId, Severity> Suppressed(IReadOnlyCollection<string>? skippedCodes)
    {
        Dictionary<MessageId, Severity> suppressed = [];
        foreach (string code in skippedCodes ?? [])
        {
            if (MessageIds.TryParse(code, out MessageId id))
            {
                suppressed[id] = Severity.Suppressed;
            }
        }

        return suppressed;
    }

    // Collects the messages of the checker as validation results and tells how far the check is.
    private sealed partial class BookValidationReport : ReportBase
    {
        private readonly List<ValidationResult> _results = [];
        private readonly HashSet<string> _seenResources = new(StringComparer.Ordinal);
        private readonly int _total;
        private readonly ValidationRun _run;
        private readonly EpubCheckFixes _fixes;

        public BookValidationReport(Book book, CultureInfo culture, Dictionary<MessageId, Severity> suppressed, bool includeUsage, int total, ValidationRun run)
        {
            _fixes = new EpubCheckFixes(book);
            _total = total;
            _run = run;
            SetLocale(culture);
            if (suppressed.Count > 0)
            {
                Dictionary = new SeverityOverrideMessageDictionary(Dictionary, suppressed);
            }

            ReportingLevel = includeUsage ? Severity.Usage : Severity.Info;
        }

        public IReadOnlyList<ValidationResult> Results => _results;

        public override void Info(string? resource, FeatureKind feature, string? value)
        {
            // The checks tell about every resource they look at: that is the progress, and a place to stop.
            _run.Cancellation.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(resource) && _seenResources.Add(resource))
            {
                _run.Progress?.Report(new ValidationProgress(ValidationStage.EpubCheck, Math.Min(_seenResources.Count, _total), _total));
            }
        }

        protected override void OnMessage(Message message, EpubLocation location, object?[] args)
        {
            _run.Cancellation.ThrowIfCancellationRequested();
            string text = Whitespace().Replace(Dictionary.Format(message, args), " ").Trim();
            _results.Add(new ValidationResult(
                ToSeverity(message.Severity),
                location.Path == PublicationName ? string.Empty : location.Path,
                location.Line,
                location.Column,
                text,
                message.Id.ToText(),
                _fixes.Create(message.Id, location, args)));
        }

        private static ValidationSeverity ToSeverity(Severity severity) => severity switch
        {
            Severity.Fatal => ValidationSeverity.Fatal,
            Severity.Error => ValidationSeverity.Error,
            Severity.Warning => ValidationSeverity.Warning,
            Severity.Usage => ValidationSeverity.Usage,
            _ => ValidationSeverity.Info,
        };

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
