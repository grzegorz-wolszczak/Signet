using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class OCFCheckerTests
{
    [Fact]
    public void Check_DuplicateCanonicalNames_ReportsDuplicateEntry()
    {
        // Arrange
        using MemoryEpubSource source = new(new[]
        {
            new KeyValuePair<string, byte[]>("mimetype", "application/epub+zip"u8.ToArray()),
            new KeyValuePair<string, byte[]>("META-INF/container.xml", []),
            new KeyValuePair<string, byte[]>("m\u00FCnchen.xhtml", []),
            new KeyValuePair<string, byte[]>("mu\u0308nchen.xhtml", []),
        });
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.OPF_060);
    }

    [Fact]
    public void Check_MissingContainerFile_ReportsFatalError()
    {
        // Arrange
        using MemoryEpubSource source = new(new[]
        {
            new KeyValuePair<string, byte[]>("mimetype", "application/epub+zip"u8.ToArray()),
        });
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.RSC_002);
    }
}
