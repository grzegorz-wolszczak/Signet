using System.Text.RegularExpressions;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// The report that prints to the console (upstream's <c>DefaultReportImpl</c>): <c>SEVERITY(ID): file/path(line,column): text</c>
/// on standard error, except the usages, which go to standard output; and the "Validating using EPUB version" line.
/// </summary>
internal sealed partial class ConsoleReport : ReportBase
{
    private readonly ToolOutput _output;
    private readonly ToolMessages _messages;

    public ConsoleReport(string epubName, ToolOutput output, ToolMessages messages)
    {
        _output = output;
        _messages = messages;
        EpubFileName = PathUtil.RemoveWorkingDirectory(epubName);
    }

    public override void Info(string? resource, FeatureKind feature, string? value)
    {
        // Upstream prints this line when the reporting level lets infos through.
        if (feature == FeatureKind.FormatVersion && ReportingLevel <= Severity.Info)
        {
            _output.Println(_messages.Get("validating_version_message", value));
        }
    }

    protected override void OnMessage(Message message, EpubLocation location, object?[] args)
    {
        string text = FormatMessage(message, location, args);
        if (message.Severity == Severity.Usage)
        {
            _output.PrintlnAlways(text);
        }
        else
        {
            _output.PrintError(text);
        }
    }

    internal string FormatMessage(Message message, EpubLocation location, object?[] args)
    {
        string epubFileName = PathUtil.RemoveWorkingDirectory(EpubFileName ?? string.Empty);
        string fileName = epubFileName.EndsWith(location.Path, System.StringComparison.Ordinal) ? string.Empty : "/" + location.Path;
        string text = Whitespace().Replace(Dictionary.Format(message, args), " ");
        return $"{message.Severity.ToText()}({message.Id.ToText()}): {epubFileName}{fileName}({location.Line},{location.Column}): {text}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
