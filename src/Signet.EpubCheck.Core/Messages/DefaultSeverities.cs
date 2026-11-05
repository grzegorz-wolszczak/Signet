using System;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>The default severity of every message (upstream's <c>DefaultSeverities</c>); a custom messages file can override it.</summary>
public static partial class DefaultSeverities
{
    private static readonly Severity[] s_values = Create();

    /// <summary>The default severity of <paramref name="id"/>.</summary>
    public static Severity Get(MessageId id)
    {
        int index = (int)id;
        return index >= 0 && index < s_values.Length ? s_values[index] : throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }
}
