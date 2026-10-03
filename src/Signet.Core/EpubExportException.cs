using System;

namespace Signet.Core;

/// <summary>
/// Thrown when writing an EPUB to disk fails (the output file cannot be created, a packaging
/// error).
/// </summary>
public sealed class EpubExportException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public EpubExportException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public EpubExportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EpubExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
