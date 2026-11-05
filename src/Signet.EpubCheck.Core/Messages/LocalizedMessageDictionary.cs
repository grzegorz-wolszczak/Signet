using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>The messages with their default severities and the texts of one language (upstream's <c>LocalizedMessageDictionary</c>).</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Keeps the name of the upstream type.")]
public sealed class LocalizedMessageDictionary : IMessageDictionary
{
    private readonly LocalizedMessages _messages;

    /// <summary>Creates the dictionary for <paramref name="culture"/> (<see cref="CultureInfo.CurrentUICulture"/> when <c>null</c>).</summary>
    public LocalizedMessageDictionary(CultureInfo? culture = null)
    {
        _messages = LocalizedMessages.GetInstance(culture);
    }

    /// <summary>The language of the texts: <c>en</c> or <c>pl</c>.</summary>
    public string Language => _messages.Language;

    /// <inheritdoc />
    public Message GetMessage(MessageId id) => _messages.GetMessage(id);

    /// <inheritdoc />
    public string Format(Message message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _messages.Format(message, args);
    }
}
