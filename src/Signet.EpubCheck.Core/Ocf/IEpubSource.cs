using System;
using System.Collections.Generic;
using System.IO;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>A read-only source of the resources that make up an EPUB publication.</summary>
public interface IEpubSource : IDisposable
{
    /// <summary>Whether the source is a packaged ZIP container.</summary>
    bool IsPackaged { get; }

    /// <summary>The resources in their source order.</summary>
    IReadOnlyList<EpubSourceEntry> Entries { get; }

    /// <summary>Opens a resource for reading.</summary>
    Stream OpenRead(EpubSourceEntry entry);
}
