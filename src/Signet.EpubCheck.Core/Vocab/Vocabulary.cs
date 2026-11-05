using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Vocab;

/// <summary>A property vocabulary.</summary>
public interface IVocabulary
{
    string Uri { get; }
    bool IsDeprecated(string prefix);
    VocabProperty? Lookup(string name);
}

/// <summary>An EPUB property resolved through a vocabulary.</summary>
public sealed record VocabProperty(string Name, string VocabularyUri, string Prefix)
{
    public string PrefixedName => string.IsNullOrEmpty(Prefix) ? Name : $"{Prefix}:{Name}";
    public string FullName => VocabularyUri + Name;
}

/// <summary>A vocabulary that accepts every property name.</summary>
public class UncheckedVocabulary(string uri, string prefix) : IVocabulary
{
    public string Uri { get; } = uri;
    public virtual bool IsDeprecated(string testedPrefix) => false;
    public VocabProperty Lookup(string name) => new(name, Uri, prefix);
}

/// <summary>Parses OPF prefix declarations and property values.</summary>
public static class VocabularyParser
{
    private static readonly Regex PrefixName = new("^[A-Za-z_][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> ParsePrefixMappings(string? value, IReport report, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        if (value is null)
        {
            return new ReadOnlyDictionary<string, string>(mappings);
        }

        string[] tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < tokens.Length; index += 2)
        {
            string token = tokens[index];
            if (!token.EndsWith(':') || token.Length == 1 || !PrefixName.IsMatch(token[..^1]))
            {
                report.Message(token == ":" ? MessageId.OPF_004a : MessageId.OPF_004b, location, token.TrimEnd(':'));
                continue;
            }

            if (index + 1 == tokens.Length)
            {
                report.Message(MessageId.OPF_005, location, token[..^1]);
                break;
            }

            string uri = tokens[index + 1];
            if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _))
            {
                report.Message(MessageId.OPF_006, location, uri, token[..^1]);
                continue;
            }

            mappings.TryAdd(token[..^1], uri);
        }

        return new ReadOnlyDictionary<string, string>(mappings);
    }

    public static IReadOnlyDictionary<string, IVocabulary> ParsePrefixDeclaration(string? value, IReadOnlyDictionary<string, IVocabulary> predefined, IReadOnlyDictionary<string, IVocabulary> known, ISet<string> forbidden, IReport report, EpubLocation location)
    {
        Dictionary<string, IVocabulary> result = new(predefined, StringComparer.Ordinal);
        foreach ((string prefix, string uri) in ParsePrefixMappings(value, report, location))
        {
            if (prefix == "_")
            {
                report.Message(MessageId.OPF_007a, location);
            }
            else if (forbidden.Contains(uri))
            {
                report.Message(MessageId.OPF_007b, location, prefix);
            }
            else if (uri == "http://purl.org/dc/elements/1.1/")
            {
                report.Message(MessageId.OPF_007c, location);
            }
            else
            {
                result.TryGetValue(prefix, out IVocabulary? reserved);
                if (reserved is not null && reserved.Uri != uri)
                {
                    report.Message(MessageId.OPF_007, location, prefix);
                }

                result[prefix] = known.TryGetValue(uri, out IVocabulary? vocabulary) ? vocabulary : new UncheckedVocabulary(uri, prefix);
            }
        }

        return new ReadOnlyDictionary<string, IVocabulary>(result);
    }

    public static IReadOnlySet<VocabProperty> ParsePropertyList(string? value, IReadOnlyDictionary<string, IVocabulary> vocabularies, IReport report, EpubLocation location)
    {
        HashSet<VocabProperty> result = [];
        if (value is null)
        {
            return result;
        }

        foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = token.IndexOf(':');
            string prefix = colon < 0 ? string.Empty : token[..colon];
            string name = colon < 0 ? token : token[(colon + 1)..];
            if (colon >= 0 && (prefix.Length == 0 || name.Length == 0))
            {
                report.Message(MessageId.OPF_026, location, token);
            }
            else if (!vocabularies.TryGetValue(prefix, out IVocabulary? vocabulary))
            {
                report.Message(MessageId.OPF_028, location, prefix);
            }
            else if (vocabulary.Lookup(name) is VocabProperty property)
            {
                result.Add(property);
            }
            else
            {
                report.Message(MessageId.OPF_027, location, token);
            }
        }

        return result;
    }
}
