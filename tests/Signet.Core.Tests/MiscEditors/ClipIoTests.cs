using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="ClipIo"/> and <see cref="ClipStore"/>.</summary>
public sealed class ClipIoTests
{
    private static readonly ClipEntry[] Sample =
    {
        new(IsGroup: true, "Example Clips/", "Example Clips", ""),
        new(IsGroup: false, "Example Clips/p", "p", "<p>\\1</p>"),
        new(IsGroup: false, "Prosty, z przecinkiem", "Prosty, z przecinkiem", "<span class=\"x\">\\1</span>"),
    };

    [Fact]
    public void WriteJson_ThenReadJson_RoundTrips()
    {
        IReadOnlyList<ClipEntry> parsed = ClipIo.ReadJson(ClipIo.WriteJson(Sample));

        parsed.Should().BeEquivalentTo(Sample, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ReadJson_InvalidContent_ReturnsEmpty()
    {
        ClipIo.ReadJson("{ not json").Should().BeEmpty();
        ClipIo.ReadJson("{}").Should().BeEmpty();
        ClipIo.ReadJson(null).Should().BeEmpty();
    }

    [Fact]
    public void Store_SaveThenReloadWithNewInstance_PersistsEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), $"signet-clips-{Guid.NewGuid():N}.json");
        try
        {
            new ClipStore(path).Save(Sample);

            new ClipStore(path).Load().Should().BeEquivalentTo(Sample, o => o.WithStrictOrdering());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Store_Load_MissingFile_ReturnsEmpty()
    {
        new ClipStore(Path.Combine("nonexistent-dir", "missing.json")).Load().Should().BeEmpty();
    }
}
