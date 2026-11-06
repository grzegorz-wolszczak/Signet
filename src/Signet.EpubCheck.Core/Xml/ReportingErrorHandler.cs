using System;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// Reports the errors of the XML parser and of the schemas as EPUBCheck's messages (upstream's
/// <c>ReportingErrorHandler</c>), at the line and column of the error in the checked file:
/// <list type="bullet">
/// <item>a warning is <c>RSC-017</c> (<c>RSC-024</c> for an informative schema);</item>
/// <item>an error is <c>RSC-005</c> (<c>RSC-025</c> for an informative schema), unless the message starts with
/// <c>WARNING:</c> — then it is <c>RSC-017</c> / <c>RSC-024</c> — or <c>USAGE:</c> — then <c>RSC-036</c>; the prefix
/// and the space after it are dropped. The Schematron schemas mark their warnings and usage notes so;</item>
/// <item>a fatal error is <c>RSC-016</c>.</item>
/// </list>
/// </summary>
public sealed class ReportingErrorHandler : ISaxErrorHandler
{
    private const string WarningPrefix = "WARNING:";
    private const string UsagePrefix = "USAGE:";

    // Upstream cuts the prefix and the one character after it (the space).
    private const int PrefixSeparatorLength = 1;

    private readonly IReport _report;
    private readonly EpubLocation _location;
    private readonly bool _normative;

    /// <summary>Reports to <paramref name="report"/> at <paramref name="location"/> (the checked file; the line and column
    /// come from each error); <paramref name="normative"/> is <c>false</c> for an informative schema.</summary>
    public ReportingErrorHandler(IReport report, EpubLocation location, bool normative = true)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _location = location ?? throw new ArgumentNullException(nameof(location));
        _normative = normative;
    }

    /// <inheritdoc />
    public void OnWarning(SaxParseException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _report.Message(_normative ? MessageId.RSC_017 : MessageId.RSC_024, At(exception), exception.Message);
    }

    /// <inheritdoc />
    public void OnError(SaxParseException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = exception.Message.Trim();
        if (message.StartsWith(WarningPrefix, StringComparison.Ordinal))
        {
            _report.Message(_normative ? MessageId.RSC_017 : MessageId.RSC_024, At(exception), AfterPrefix(message, WarningPrefix));
        }
        else if (message.StartsWith(UsagePrefix, StringComparison.Ordinal))
        {
            _report.Message(MessageId.RSC_036, At(exception), AfterPrefix(message, UsagePrefix));
        }
        else
        {
            _report.Message(_normative ? MessageId.RSC_005 : MessageId.RSC_025, At(exception), message);
        }
    }

    /// <inheritdoc />
    public void OnFatalError(SaxParseException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _report.Message(MessageId.RSC_016, At(exception), exception.Message);
    }

    // Upstream's substring throws when nothing follows the prefix; the port gives an empty text.
    private static string AfterPrefix(string message, string prefix) =>
        message[Math.Min(prefix.Length + PrefixSeparatorLength, message.Length)..];

    private EpubLocation At(SaxParseException exception) => _location.At(exception.LineNumber, exception.ColumnNumber);
}
