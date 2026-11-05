using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class DefaultSeveritiesTests
{
    [Theory]
    [InlineData(MessageId.INF_001, Severity.Info)]
    [InlineData(MessageId.ACC_001, Severity.Suppressed)]
    [InlineData(MessageId.ACC_009, Severity.Usage)]
    [InlineData(MessageId.CHK_001, Severity.Error)]
    [InlineData(MessageId.OPF_004a, Severity.Error)]
    [InlineData(MessageId.OPF_004e, Severity.Warning)]
    [InlineData(MessageId.HTM_060a, Severity.Usage)]
    [InlineData(MessageId.RSC_005, Severity.Error)]
    [InlineData(MessageId.PKG_025, Severity.Error)]
    public void Get_GivesTheSeverityUpstreamDefines(MessageId id, Severity expected)
    {
        // Act & Assert
        DefaultSeverities.Get(id).Should().Be(expected);
    }

    [Fact]
    public void Get_DistributesTheIdsLikeUpstream5_4_0()
    {
        // Act
        var counts = MessageIds.All.GroupBy(DefaultSeverities.Get).ToDictionary(g => g.Key, g => g.Count());

        // Assert
        counts.Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<Severity, int>
        {
            [Severity.Error] = 135,
            [Severity.Fatal] = 8,
            [Severity.Info] = 5,
            [Severity.Suppressed] = 84,
            [Severity.Usage] = 48,
            [Severity.Warning] = 43,
        });
    }
}
