using System.Collections.Generic;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>A message that was reported, with where and with which arguments.</summary>
/// <param name="Message">The message (its severity is the one it was reported with).</param>
/// <param name="Location">Where it points.</param>
/// <param name="Arguments">The arguments of its text.</param>
/// <param name="Text">Its text with the arguments filled in, in the language of the report's dictionary.</param>
public sealed record ReportedMessage(Message Message, EpubLocation Location, IReadOnlyList<object?> Arguments, string Text);

/// <summary>A fact about a resource that was reported through <see cref="IReport.Info"/>.</summary>
/// <param name="Resource">The resource, or <c>null</c> for the publication itself.</param>
/// <param name="Feature">The kind of fact.</param>
/// <param name="Value">The value.</param>
public sealed record ReportedFeature(string? Resource, FeatureKind Feature, string? Value);

/// <summary>A report that keeps everything in lists, for the callers that look at the result themselves (the tests, Signet).</summary>
public sealed class CollectingReport : ReportBase
{
    private readonly List<ReportedMessage> _messages = [];
    private readonly List<ReportedFeature> _features = [];

    /// <summary>Creates a report with the default messages in the language of the current UI culture.</summary>
    public CollectingReport()
    {
    }

    /// <summary>Creates a report that gets its messages from <paramref name="dictionary"/>.</summary>
    public CollectingReport(IMessageDictionary dictionary)
        : base(dictionary)
    {
    }

    /// <summary>The messages that passed the reporting level, in the order they were reported.</summary>
    public IReadOnlyList<ReportedMessage> Messages => _messages;

    /// <summary>The facts reported through <see cref="IReport.Info"/>, in the order they were reported.</summary>
    public IReadOnlyList<ReportedFeature> Features => _features;

    /// <inheritdoc />
    public override void Info(string? resource, FeatureKind feature, string? value) =>
        _features.Add(new ReportedFeature(resource, feature, value));

    /// <inheritdoc />
    protected override void OnMessage(Message message, EpubLocation location, object?[] args) =>
        _messages.Add(new ReportedMessage(message, location, args, Dictionary.Format(message, args) ?? string.Empty));
}
