using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>A message the checker reported, as the assertion steps look at it (upstream's <c>MessageInfo</c>).</summary>
internal sealed record MessageInfo(Severity Severity, MessageId? Id, string? Path, int Line, int Column, string Message)
{
    public override string ToString()
    {
        string where = Line > 0 ? $"({Line}{(Column > 0 ? "," + Column : string.Empty)})" : string.Empty;
        return $"{Severity.ToText()} {Id?.ToText()}: {Message}{(Path is null ? string.Empty : " | in " + Path)}{where}";
    }
}

/// <summary>
/// The report a scenario's checker writes to (upstream's <c>TestReport</c>): it keeps every message, and the steps
/// <i>consume</i> the ones they assert, so that "no other errors" can look at what is left.
/// </summary>
internal sealed class ScenarioReport : ReportBase
{
    private List<MessageInfo> _messages = [];

    public ScenarioReport()
        : base(new LocalizedMessageDictionary(System.Globalization.CultureInfo.GetCultureInfo("en")))
    {
    }

    /// <summary>The format of the report the checker also writes (<c>JSON</c>, <c>XML</c>, <c>XMP</c>), or <c>default</c>.</summary>
    public string Format { get; set; } = "default";

    /// <summary>What the report writer wrote (empty until the writers exist).</summary>
    public StringWriter Output { get; } = new();

    public IReadOnlyList<MessageInfo> AllMessages => _messages.ToList();

    public IEnumerable<MessageInfo> GetAll(Severity severity) => _messages.Where(m => m.Severity == severity);

    /// <summary>Takes the first message with the id out of the report; <c>null</c> when there is none.</summary>
    public MessageInfo? Consume(MessageId id)
    {
        MessageInfo? first = _messages.FirstOrDefault(m => m.Id == id);
        if (first is not null)
        {
            _messages.Remove(first);
        }

        return first;
    }

    public List<MessageInfo> ConsumeAll(MessageId id) => Take(m => m.Id == id);

    public List<MessageInfo> ConsumeAll(Severity severity) => Take(m => m.Severity == severity);

    public override void Info(string? resource, FeatureKind feature, string? value) =>
        _messages.Add(new MessageInfo(Severity.Info, null, resource, 0, 0, FixMessage($"[{feature.ToText()}] {value}")));

    protected override void OnMessage(Message message, EpubLocation location, object?[] args) =>
        _messages.Add(new MessageInfo(
            message.Severity, message.Id, location.Path, location.Line, location.Column, FixMessage(Dictionary.Format(message, args))));

    private List<MessageInfo> Take(Func<MessageInfo, bool> predicate)
    {
        List<MessageInfo> taken = _messages.Where(predicate).ToList();
        _messages = _messages.Where(m => !predicate(m)).ToList();
        return taken;
    }

    // Upstream collapses every run of white space of a message to one space.
    private static string FixMessage(string? message) => message is null ? "No message" : Regex.Replace(message, @"\s+", " ");
}
