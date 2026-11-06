using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Tests.Vocab;

/// <summary>What upstream's <c>ValidationReport</c> gives its tests: the ids of the messages of one severity, in order.</summary>
internal static class VocabTestSupport
{
    public static readonly EpubLocation Location = new("test");

    public static CollectingReport NewReport() => new() { ReportingLevel = Severity.Usage };

    public static List<MessageId> Ids(this CollectingReport report, Severity severity) =>
        report.Messages.Where(message => message.Message.Severity == severity).Select(message => message.Message.Id).ToList();
}
