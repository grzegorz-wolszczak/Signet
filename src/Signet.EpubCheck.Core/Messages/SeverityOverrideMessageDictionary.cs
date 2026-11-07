using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>
/// A dictionary that reports some messages with another severity (<see cref="Severity.Suppressed"/> switches a message off) and
/// leaves the rest to the dictionary it wraps - what <c>--customMessages</c> does with a file, for a caller that has the
/// overrides in memory.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a message dictionary, like the type of upstream it wraps.")]
public sealed class SeverityOverrideMessageDictionary : IMessageDictionary
{
    private readonly IMessageDictionary _inner;
    private readonly IReadOnlyDictionary<MessageId, Severity> _overrides;

    /// <summary>Creates the dictionary.</summary>
    /// <param name="inner">The dictionary that gives the texts and the default severities.</param>
    /// <param name="overrides">The severity each listed message gets.</param>
    public SeverityOverrideMessageDictionary(IMessageDictionary inner, IReadOnlyDictionary<MessageId, Severity> overrides)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(overrides);
        _inner = inner;
        _overrides = overrides;
    }

    /// <inheritdoc />
    public Message GetMessage(MessageId id)
    {
        Message message = _inner.GetMessage(id);
        return _overrides.TryGetValue(id, out Severity severity) ? message with { Severity = severity } : message;
    }

    /// <inheritdoc />
    public string Format(Message message, params object?[] args) => _inner.Format(message, args);
}
