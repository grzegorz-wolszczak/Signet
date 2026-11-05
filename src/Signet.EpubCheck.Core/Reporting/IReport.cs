using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>What the checks report to (upstream's <c>Report</c>): messages with a location, and facts about the publication.</summary>
public interface IReport
{
    /// <summary>The default of <see cref="MaxOfEachMessage"/>: reports with a limit keep this many messages of one id and text.</summary>
    public const int DefaultMaxOfEachMessage = 25;

    /// <summary>The dictionary that gives the severity and the text of each message.</summary>
    IMessageDictionary Dictionary { get; }

    /// <summary>The lowest severity that is reported and counted; <see cref="Severity.Info"/> by default.</summary>
    Severity ReportingLevel { get; set; }

    /// <summary>The most messages of one id and text a report in XML or JSON keeps; a negative value means no limit.</summary>
    int MaxOfEachMessage { get; set; }

    /// <summary>The name of the checked file, as the report gives it.</summary>
    string? EpubFileName { get; set; }

    /// <summary>The name of the custom messages file in use, or <c>null</c>.</summary>
    string? CustomMessageFile { get; set; }

    /// <summary>How many messages of severity <see cref="Severity.Error"/> were reported.</summary>
    int ErrorCount { get; }

    /// <summary>How many messages of severity <see cref="Severity.Warning"/> were reported.</summary>
    int WarningCount { get; }

    /// <summary>How many messages of severity <see cref="Severity.Fatal"/> were reported.</summary>
    int FatalErrorCount { get; }

    /// <summary>How many messages of severity <see cref="Severity.Info"/> were reported.</summary>
    int InfoCount { get; }

    /// <summary>How many messages of severity <see cref="Severity.Usage"/> were reported.</summary>
    int UsageCount { get; }

    /// <summary>Reports a message; <paramref name="args"/> fill the placeholders of its text.</summary>
    void Message(MessageId id, EpubLocation location, params object?[] args);

    /// <summary>Reports a fact about a resource of the publication (or the publication itself when <paramref name="resource"/> is <c>null</c>).</summary>
    void Info(string? resource, FeatureKind feature, string? value);

    /// <summary>Called before the first message.</summary>
    void Initialize();

    /// <summary>Called after the last message; writes what the report writes. Returns upstream's result code of the report (0 when it wrote nothing wrong).</summary>
    int Generate();
}
