using System.IO;
using System.Linq;
using System.Reflection;

namespace Signet.EpubCheck.Core.Tests.TestSupport;

/// <summary>The copy of upstream's test resources, <c>tests/corpus/epubcheck</c>.</summary>
internal static class UpstreamCorpus
{
    public static string Root { get; } = Path.GetFullPath(Path.Combine(
        typeof(UpstreamCorpus).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CorpusRoot").Value!,
        "epubcheck"));

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}
