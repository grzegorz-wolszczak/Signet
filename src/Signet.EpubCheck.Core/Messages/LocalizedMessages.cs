using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>
/// The texts of the messages in one language (upstream's <c>LocalizedMessages</c>): English (the neutral resource) or
/// Polish. Any other culture gets English. The texts live in <c>EpubCheckMessages.resx</c> and
/// <c>EpubCheckMessages.pl.resx</c> under the keys of upstream's bundle: <c>OPF_014</c> is the text, <c>OPF_035_SUG</c>
/// its suggestion, <c>OBS_001.element</c> a localized argument.
/// </summary>
public sealed class LocalizedMessages
{
    private static readonly ResourceManager s_manager =
        new("Signet.EpubCheck.Core.Messages.EpubCheckMessages", typeof(LocalizedMessages).Assembly);

    // The texts of the port itself (not in upstream's bundle): the one for a message that has no text.
    private static readonly ResourceManager s_portManager =
        new("Signet.EpubCheck.Core.Messages.PortMessages", typeof(LocalizedMessages).Assembly);

    private static readonly HashSet<string> s_supported = ["en", "pl"];

    private static readonly ConcurrentDictionary<string, LocalizedMessages> s_instances = new(StringComparer.Ordinal);

    private readonly CultureInfo _culture;
    private readonly ConcurrentDictionary<MessageId, Message> _messages = new();

    private LocalizedMessages(CultureInfo culture)
    {
        _culture = culture;
    }

    /// <summary>The languages that have their own texts (the others get English).</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "pl"];

    /// <summary>The raw resources, for the tests that compare the languages.</summary>
    internal static ResourceManager Manager => s_manager;

    /// <summary>The raw resources of the port's own texts, for the tests that compare the languages.</summary>
    internal static ResourceManager PortManager => s_portManager;

    /// <summary>The language of the texts: <c>en</c> or <c>pl</c>.</summary>
    public string Language => _culture.TwoLetterISOLanguageName;

    /// <summary>The texts for <paramref name="culture"/> (<see cref="CultureInfo.CurrentUICulture"/> when <c>null</c>); English when its language has no texts of its own.</summary>
    public static LocalizedMessages GetInstance(CultureInfo? culture = null)
    {
        string language = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        if (!s_supported.Contains(language))
        {
            language = "en";
        }

        return s_instances.GetOrAdd(language, static l => new LocalizedMessages(CultureInfo.GetCultureInfo(l)));
    }

    /// <summary>The message of <paramref name="id"/> with its default severity and the texts of this language.</summary>
    public Message GetMessage(MessageId id) =>
        _messages.GetOrAdd(id, key => new Message(key, DefaultSeverities.Get(key), GetString(key.ToString()), GetSuggestion(key)));

    /// <summary>
    /// What a message that has no text of its own (77 ids have none, all <see cref="Severity.Suppressed"/> by default) says when it
    /// is reported anyway: that there is no description for it. Upstream prints an empty text.
    /// </summary>
    public string NoDescription(MessageId id) =>
        JavaFormat.Format(s_portManager.GetString("NoDescription", _culture) ?? "No description is available for %1$s.", id.ToText());

    /// <summary>The suggestion of the message, or the empty string.</summary>
    public string GetSuggestion(MessageId id) => GetString(id + "_SUG");

    /// <summary>
    /// The suggestion for <paramref name="key"/> (<c>OPF_086_SUG.&lt;key&gt;</c>); when there is none, the <c>default</c> one,
    /// and when there is none either, the plain suggestion of the message.
    /// </summary>
    public string GetSuggestion(MessageId id, string? key)
    {
        string? specific = key is null ? null : s_manager.GetString(id + "_SUG." + key, _culture);
        return specific ?? s_manager.GetString(id + "_SUG.default", _culture) ?? GetSuggestion(id);
    }

    /// <summary>
    /// Fills the placeholders of the message's text. When the message has a localized argument (<see cref="MessageIds.HasLocalizedArgument"/>)
    /// and an argument names one in the resources (<c>OBS_001.element</c>), that argument is replaced by its text, filled
    /// with the arguments after it, and the arguments after it are dropped. A text that cannot be filled comes back
    /// with <c> !!! </c> and the reason, as upstream's does.
    /// </summary>
    public string Format(Message message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(args);
        if (message.Text.Length == 0)
        {
            return NoDescription(message.Id);
        }

        try
        {
            object?[] actual = args;
            if (message.Id.HasLocalizedArgument())
            {
                actual = LocalizeArgument(message.Id, args);
            }

            return JavaFormat.Format(message.Text, actual);
        }
        catch (FormatException ex)
        {
            return message.Text + " !!! " + ex.Message;
        }
    }

    private object?[] LocalizeArgument(MessageId id, object?[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            object? argument = args[i];
            if (argument is null)
            {
                continue;
            }

            string? localized = s_manager.GetString(id + "." + argument, _culture);
            if (localized is null)
            {
                continue;
            }

            if (i < args.Length - 1)
            {
                localized = JavaFormat.Format(localized, args[(i + 1)..]);
            }

            object?[] result = new object?[i + 1];
            Array.Copy(args, result, i);
            result[i] = localized;
            return result;
        }

        return args;
    }

    private string GetString(string key) => s_manager.GetString(key, _culture) ?? string.Empty;
}
