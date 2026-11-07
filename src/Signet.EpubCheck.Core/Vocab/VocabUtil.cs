using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Vocab;

/// <summary>Where properties are parsed: the report, the media type of the document and the language of the suggestions.</summary>
/// <param name="Report">Where the problems go.</param>
/// <param name="MimeType">The media type of the document the properties are in (<c>OPF-087</c> depends on it).</param>
/// <param name="Locale">The language of the suggestions of <c>OPF-086</c>; <c>null</c> for the current UI culture.</param>
public sealed record VocabContext(IReport Report, string? MimeType = null, CultureInfo? Locale = null);

/// <summary>Parses property values and prefix declarations against vocabularies (upstream's <c>VocabUtil</c>).</summary>
public static class VocabUtil
{
    private const string UndeclaredPrefix = "_";
    private const string OutdatedArgument = "property";

    /// <summary>A vocabulary without properties.</summary>
    public static readonly IVocab EmptyVocab = new EnumVocab<Empty>(string.Empty, null, []);

    private enum Empty
    {
    }

    /// <summary>
    /// Parses one property (<paramref name="value"/> must not be a list: <c>OPF-025</c>); <c>null</c> when there is
    /// none or it is not valid.
    /// </summary>
    public static VocabProperty? ParseProperty(string? value, IReadOnlyDictionary<string, IVocab> vocabs, VocabContext context, EpubLocation location) =>
        ParseProperties(value, vocabs, isList: false, context, location).FirstOrDefault();

    /// <summary>
    /// Parses a white-space separated list of properties: each once, in their order (upstream's ordered set); the ones
    /// that are not valid are reported and left out.
    /// </summary>
    public static IReadOnlyList<VocabProperty> ParsePropertyList(string? value, IReadOnlyDictionary<string, IVocab> vocabs, VocabContext context, EpubLocation location) =>
        ParseProperties(value, vocabs, isList: true, context, location);

    /// <summary>
    /// The vocabularies of a <c>prefix</c> attribute: <paramref name="predefined"/> and the declared prefixes, each with the
    /// <paramref name="known"/> vocabulary of its URI or an unchecked one. Prefixes that must not be declared are reported.
    /// </summary>
    public static IReadOnlyDictionary<string, IVocab> ParsePrefixDeclaration(
        string? value,
        IReadOnlyDictionary<string, IVocab> predefined,
        IReadOnlyDictionary<string, IVocab> known,
        IReadOnlySet<string> forbidden,
        IReport report,
        EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(predefined);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(forbidden);
        Dictionary<string, IVocab> vocabs = new(predefined, StringComparer.Ordinal);
        foreach ((string prefix, string uri) in PrefixDeclarationParser.ParsePrefixMappings(value, report, location))
        {
            if (prefix == UndeclaredPrefix)
            {
                report.Message(MessageId.OPF_007a, location);
            }
            else if (forbidden.Contains(uri))
            {
                report.Message(MessageId.OPF_007b, location, prefix);
            }
            else if (uri == DCMESVocab.Uri)
            {
                report.Message(MessageId.OPF_007c, location);
            }
            else
            {
                if (predefined.TryGetValue(prefix, out IVocab? reserved))
                {
                    if ((reserved.Uri ?? string.Empty) != uri)
                    {
                        report.Message(MessageId.OPF_007, location, prefix);
                    }

                    if (reserved.IsDeprecated(prefix))
                    {
                        report.Message(MessageId.OPF_086c, location, prefix);
                    }
                }

                vocabs[prefix] = known.TryGetValue(uri, out IVocab? vocab) ? vocab : new UncheckedVocab(uri, prefix);
            }
        }

        return vocabs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static List<VocabProperty> ParseProperties(string? value, IReadOnlyDictionary<string, IVocab> vocabs, bool isList, VocabContext context, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(vocabs);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(location);
        List<VocabProperty> result = [];
        if (value is null)
        {
            return result;
        }

        IReport report = context.Report;
        List<string> properties = SplitOnWhitespace(value);
        if (!isList && properties.Count > 1)
        {
            report.Message(MessageId.OPF_025, location, value);
            return result;
        }

        LocalizedMessages? messages = null;
        foreach (string property in properties)
        {
            // prefix:name; a colon with nothing before or after it is malformed.
            int colon = property.IndexOf(':', StringComparison.Ordinal);
            string prefix = colon < 0 ? string.Empty : property[..colon];
            string name = colon < 0 ? property : property[(colon + 1)..];
            if (colon >= 0 && (prefix.Length == 0 || name.Length == 0))
            {
                report.Message(MessageId.OPF_026, location, property);
                continue;
            }

            if (!vocabs.TryGetValue(prefix, out IVocab? vocab))
            {
                report.Message(MessageId.OPF_028, location, prefix);
                continue;
            }

            VocabProperty? found = vocab.Lookup(name);
            if (vocab.IsDeprecated(prefix))
            {
                report.Message(MessageId.OPF_086c, location, prefix);
            }

            if (found is null)
            {
                report.Message(MessageId.OPF_027, location, property);
                continue;
            }

            if (found.IsDeprecated)
            {
                // The replacement is the suggestion OPF_086_SUG.<name> (OPF_086b for structural semantics).
                MessageId id = found.VocabUri == StructureVocab.Uri ? MessageId.OPF_086b : MessageId.OPF_086;
                messages ??= LocalizedMessages.GetInstance(context.Locale);
                report.Message(id, location, property, messages.GetSuggestion(id, found.Name));
            }

            if (found.IsOutdated)
            {
                report.Message(MessageId.OBS_001, location, OutdatedArgument, property);
            }

            if (!found.IsAllowed(context.MimeType))
            {
                report.Message(MessageId.OPF_087, location, property, context.MimeType);
            }

            if (!result.Contains(found))
            {
                result.Add(found);
            }
        }

        return result;
    }

    // The tokens between runs of white space (Java's \s+: ASCII white space only), none empty. A value of one token comes back as the very same string.
    private static List<string> SplitOnWhitespace(string value)
    {
        List<string> tokens = [];
        int start = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r')
            {
                if (start >= 0)
                {
                    tokens.Add(value[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            tokens.Add(value[start..]);
        }

        return tokens;
    }
}
