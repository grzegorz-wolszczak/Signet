using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// A message of the XML, XMP and JSON reports (upstream's <c>CheckMessage</c>): all the reports of one id and text, with where
/// they were reported - at most <c>MaxOfEachMessage</c> locations, the rest only counted.
/// </summary>
public sealed class CheckMessage
{
    private readonly List<EpubLocation> _locations = [];
    private readonly int _maxCount;

    private CheckMessage(Message message, string text, int maxCount)
    {
        Id = message.Id.ToText();
        Severity = message.Severity;
        Text = text;
        Suggestion = message.Suggestion.Length == 0 ? null : message.Suggestion;
        _maxCount = maxCount;
    }

    /// <summary>The id of the message (<c>OPF-003</c>).</summary>
    public string Id { get; }

    /// <summary>The severity the message was reported with.</summary>
    public Severity Severity { get; }

    /// <summary>The text with the arguments filled in.</summary>
    public string Text { get; }

    /// <summary>The suggestion, or <c>null</c> when the message has none.</summary>
    public string? Suggestion { get; }

    /// <summary>How many locations were left out because of the limit.</summary>
    public int AdditionalLocations { get; private set; }

    /// <summary>The locations that were kept.</summary>
    public IReadOnlyList<EpubLocation> Locations => _locations;

    /// <summary>
    /// Adds a report of <paramref name="message"/> to <paramref name="messages"/>: to the entry that has the same id and text,
    /// or to a new one. A location that is already there is not added again.
    /// </summary>
    public static CheckMessage Add(List<CheckMessage> messages, int maxCount, Message message, string text, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(location);
        string id = message.Id.ToText();
        CheckMessage? found = messages.Find(m => m.Id == id && m.Text == text);
        if (found is null)
        {
            found = new CheckMessage(message, text, maxCount);
            messages.Add(found);
        }

        found.AddLocation(location);
        return found;
    }

    /// <summary>Sorts the locations by path, line, column and context.</summary>
    public void SortLocations() => _locations.Sort(static (a, b) => a.CompareTo(b));

    /// <summary>Orders by id, severity, text, suggestion and the numbers of locations, like upstream.</summary>
    public int CompareTo(CheckMessage? other)
    {
        if (other is null)
        {
            return 1;
        }

        int comparison = string.CompareOrdinal(Id, other.Id);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = ((int)Severity).CompareTo((int)other.Severity);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(Text, other.Text);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(Suggestion, other.Suggestion);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = AdditionalLocations.CompareTo(other.AdditionalLocations);
        return comparison != 0 ? comparison : _locations.Count.CompareTo(other._locations.Count);
    }

    /// <summary>A location as the XML and XMP reports word it: <c>path (line-column)</c>, or the path alone when the line and column are unknown.</summary>
    internal static string DescribeLocation(EpubLocation location) =>
        location.Line > 0 || location.Column > 0 ? $"{location.Path} ({location.Line}-{location.Column})" : location.Path;

    /// <summary>The described locations without a repeat of the one before, as the XMP report lists them.</summary>
    internal IEnumerable<string> DescribeDistinctNeighbours()
    {
        string previous = string.Empty;
        foreach (string value in _locations.Select(DescribeLocation))
        {
            if (previous != value)
            {
                yield return value;
                previous = value;
            }
        }
    }

    private void AddLocation(EpubLocation location)
    {
        if (_locations.Contains(location))
        {
            return;
        }

        if (_maxCount < 0 || _locations.Count < _maxCount)
        {
            _locations.Add(location);
        }
        else
        {
            AdditionalLocations++;
        }
    }
}
