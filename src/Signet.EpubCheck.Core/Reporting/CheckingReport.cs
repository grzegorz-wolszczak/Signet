using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// The report in JSON (<c>--json</c>; upstream's <c>CheckingReport</c>): the checker, the publication, the files (items) and the
/// messages, written when <see cref="Generate"/> is called, in the layout and the field order of upstream's Jackson output.
/// </summary>
public sealed class CheckingReport : ReportBase
{
    private const string NoManifestReference = "ePubCheck.NoManifestRef:";

    private readonly TextWriter _out;
    private readonly bool _closeWriter;
    private readonly CheckerMetadata _checker = new();
    private readonly PublicationMetadata _publication = new();
    private readonly Dictionary<string, ItemMetadata> _items = new(StringComparer.Ordinal);
    private readonly List<CheckMessage> _messages = [];

    /// <summary>Creates the report for the publication <paramref name="epubName"/>, to be written to <paramref name="output"/>.</summary>
    /// <param name="output">Where the JSON goes.</param>
    /// <param name="epubName">The checked file or folder, as the report names it.</param>
    /// <param name="closeWriter">Whether <see cref="Generate"/> disposes <paramref name="output"/> (a file does; the console does not).</param>
    public CheckingReport(TextWriter output, string epubName, bool closeWriter = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(epubName);
        _out = output;
        _closeWriter = closeWriter;
        EpubFileName = PathUtil.RemoveWorkingDirectory(epubName);
    }

    /// <inheritdoc />
    public override void Initialize() => _checker.SetStartDate();

    /// <inheritdoc />
    public override int Generate()
    {
        _checker.SetStopDate();
        SetParameters();
        try
        {
            WriteJson();
            return 0;
        }
        catch (IOException)
        {
            return 1;
        }
        finally
        {
            if (_closeWriter)
            {
                _out.Dispose();
            }
            else
            {
                _out.Flush();
            }
        }
    }

    /// <inheritdoc />
    public override void Info(string? resource, FeatureKind feature, string? value)
    {
        _publication.HandleInfo(resource, feature, value);
        if (!string.IsNullOrEmpty(resource))
        {
            if (!_items.TryGetValue(resource, out ItemMetadata? item))
            {
                item = new ItemMetadata(resource);
                _items.Add(resource, item);
            }

            item.HandleInfo(feature, value);
        }
    }

    /// <inheritdoc />
    protected override void OnMessage(Message message, EpubLocation location, object?[] args) =>
        CheckMessage.Add(_messages, MaxOfEachMessage, message, Dictionary.Format(message, args), location);

    // Upstream's setParameters: what the items inherit from the publication, and the checker's own facts.
    private void SetParameters()
    {
        _checker.SetCheckerVersion(EpubCheckVersion.Text);
        _checker.SetFileInfo(EpubFileName ?? string.Empty);
        _checker.SetMessageTypes(_messages);
        bool defaultFixedFormat = _publication.DefaultsToFixedFormat;
        foreach (ItemMetadata item in _items.Values)
        {
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = NoManifestReference + item.FileName;
            }

            if (!item.IsSpineItem)
            {
                continue;
            }

            if (string.IsNullOrEmpty(item.RenditionLayout))
            {
                item.RenditionLayout = _publication.RenditionLayout;
            }

            if (string.IsNullOrEmpty(item.RenditionOrientation))
            {
                item.RenditionOrientation = _publication.RenditionOrientation;
            }

            if (string.IsNullOrEmpty(item.RenditionSpread))
            {
                item.RenditionSpread = _publication.RenditionSpread;
            }

            item.IsFixedFormat ??= defaultFixedFormat;
        }
    }

    private void WriteJson()
    {
        // The messages and the items are sorted before they are written, the locations of each message too.
        List<ItemMetadata> items = _items.Values.OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        _messages.Sort(static (a, b) => a.CompareTo(b));
        foreach (CheckMessage message in _messages)
        {
            message.SortLocations();
        }

        JacksonStyleJsonWriter json = new(_out);
        json.BeginObject();
        json.Name("customMessageFileName");
        json.String(CustomMessageFile);
        json.Name("checker");
        _checker.Write(json);
        json.Name("publication");
        _publication.Write(json);
        json.Name("items");
        json.BeginArray();
        foreach (ItemMetadata item in items)
        {
            item.Write(json);
        }

        json.EndArray();
        json.Name("messages");
        json.BeginArray();
        foreach (CheckMessage message in _messages)
        {
            WriteMessage(json, message);
        }

        json.EndArray();
        json.EndObject();
        _out.Flush();
    }

    private static void WriteMessage(JacksonStyleJsonWriter json, CheckMessage message)
    {
        json.BeginObject();
        json.Name("ID");
        json.String(message.Id);
        json.Name("severity");
        json.String(message.Severity.ToText());
        json.Name("message");
        json.String(message.Text);
        json.Name("additionalLocations");
        json.Number(message.AdditionalLocations);
        json.Name("locations");
        json.BeginArray();
        foreach (EpubLocation location in message.Locations)
        {
            json.BeginObject();
            json.Name("path");
            json.String(location.Path);
            json.Name("line");
            json.Number(location.Line);
            json.Name("column");
            json.Number(location.Column);
            json.Name("context");
            json.String(location.Context);
            json.EndObject();
        }

        json.EndArray();
        json.Name("suggestion");
        json.String(message.Suggestion);
        json.EndObject();
    }
}
