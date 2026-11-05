using System.IO;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class PathUtilTests
{
    [Theory, AutoData]
    public void RemoveWorkingDirectory_OfAPathUnderIt_StartsWithADot(string name)
    {
        // Arrange
        string path = Path.Combine(Directory.GetCurrentDirectory(), name, "file.txt");

        // Act
        string shortened = PathUtil.RemoveWorkingDirectory(path);

        // Assert
        shortened.Should().Be("." + Path.DirectorySeparatorChar + name + Path.DirectorySeparatorChar + "file.txt");
    }

    [Theory, AutoData]
    public void RemoveWorkingDirectory_OfAnotherPath_ReturnsItAsItIs(string name)
    {
        // Arrange
        string path = Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, "elsewhere-" + name);

        // Act & Assert
        PathUtil.RemoveWorkingDirectory(path).Should().Be(path);
        PathUtil.RemoveWorkingDirectory(string.Empty).Should().BeEmpty();
    }
}
