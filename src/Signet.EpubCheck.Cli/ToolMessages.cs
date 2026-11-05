using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// The texts of the tool itself - the help, the counters, the messages about the arguments (upstream's <c>messages.properties</c>
/// of its <c>util</c> package) - in English or Polish; any other language gets English. <c>ToolMessages.resx</c> is a copy of
/// upstream's file; <c>CliPortMessages.resx</c> holds the texts that only the port has.
/// </summary>
internal sealed class ToolMessages
{
    private static readonly ResourceManager s_upstream = new("Signet.EpubCheck.Cli.ToolMessages", typeof(ToolMessages).Assembly);
    private static readonly ResourceManager s_port = new("Signet.EpubCheck.Cli.CliPortMessages", typeof(ToolMessages).Assembly);
    private static readonly ConcurrentDictionary<string, ToolMessages> s_instances = new(StringComparer.Ordinal);

    private readonly CultureInfo _culture;

    private ToolMessages(CultureInfo culture)
    {
        _culture = culture;
    }

    /// <summary>The language of the texts: <c>en</c> or <c>pl</c>.</summary>
    public string Language => _culture.TwoLetterISOLanguageName;

    /// <summary>Whether texts in the language of <paramref name="culture"/> exist (otherwise the tool speaks English).</summary>
    public static bool IsSupported(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName is "en" or "pl";

    public static ToolMessages GetInstance(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        string language = IsSupported(culture) ? culture.TwoLetterISOLanguageName : "en";
        return s_instances.GetOrAdd(language, static l => new ToolMessages(CultureInfo.GetCultureInfo(l)));
    }

    /// <summary>The text for the key of upstream's file, with its placeholders filled by <paramref name="args"/>.</summary>
    public string Get(string key, params object?[] args) =>
        JavaFormat.Format(s_upstream.GetString(key, _culture) ?? throw new InvalidOperationException($"Missing tool message \"{key}\"."), args);

    /// <summary>The text of the port's own, for the key.</summary>
    public string GetPort(string key, params object?[] args) =>
        JavaFormat.Format(s_port.GetString(key, _culture) ?? throw new InvalidOperationException($"Missing tool message \"{key}\"."), args);
}
