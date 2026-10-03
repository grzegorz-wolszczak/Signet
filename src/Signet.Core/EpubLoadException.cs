using System;
using System.Collections.Generic;

namespace Signet.Core;

/// <summary>
/// Thrown when an EPUB cannot be loaded (corrupted ZIP, missing/invalid
/// <c>container.xml</c>, missing OPF file, etc.).
/// </summary>
public sealed class EpubLoadException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public EpubLoadException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public EpubLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EpubLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with a message and the list of warnings collected up to the error.</summary>
    public EpubLoadException(string message, IReadOnlyList<string> warnings)
        : base(message)
    {
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Warnings collected before the fatal error occurred (may be empty).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
