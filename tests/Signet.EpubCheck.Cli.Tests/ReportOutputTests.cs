using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Cli.Tests;

internal static class CorpusFiles
{
    public static string Root { get; } = System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CorpusRoot").Value!;
}

public sealed class ReportOutputTests
{
    private static string Minimal { get; } = Path.Combine(CorpusFiles.Root, "epubcheck", "reporting", "files", "minimal.epub");

    [Fact]
    public void Run_WithJsonAndAFileName_WritesTheReportThere_AndPrintsTheClosingSentence()
    {
        // Arrange
        using TempDir dir = new();
        string report = dir.Combine("report.json");

        // Act
        ToolRunner.Result result = ToolRunner.Run(Minimal, "--json", report);

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("No errors or warnings detected");
        JsonNode.Parse(File.ReadAllText(report))!["publication"]!["title"]!.GetValue<string>().Should().Be("Minimal EPUB 3.0");
    }

    [Fact]
    public void Run_WithJsonToTheConsole_PrintsTheReportAndNoClosingSentenceAboutErrors()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run(Minimal, "-j", "-");

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"title\" : \"Minimal EPUB 3.0\"");
        result.StdOut.Should().NotContain("No errors or warnings detected");
    }

    [Fact]
    public void Run_WithXmlAndNoFileName_WritesNextToTheEpub()
    {
        // Arrange
        using TempDir dir = new();
        string epub = dir.Combine("book.epub");
        File.Copy(Minimal, epub);

        // Act
        ToolRunner.Result result = ToolRunner.Run(epub, "-o");

        // Assert
        result.ExitCode.Should().Be(0);
        XDocument document = XDocument.Load(epub + "check.xml");
        document.Root!.Name.LocalName.Should().Be("jhove");
    }

    [Fact]
    public void Run_WithXmpAndAFileName_WritesAnXmpPacket()
    {
        // Arrange
        using TempDir dir = new();
        string report = dir.Combine("report.xmp");

        // Act
        ToolRunner.Run(Minimal, "-x", report);

        // Assert
        XDocument.Load(report).Root!.Name.LocalName.Should().Be("xmpmeta");
    }

    [Fact]
    public void Run_WithTwoReportFormats_FailsAndWritesNothing()
    {
        // Arrange
        using TempDir dir = new();
        string xml = dir.Combine("report.xml");
        string json = dir.Combine("report.json");

        // Act
        ToolRunner.Result result = ToolRunner.Run(Minimal, "-o", xml, "-j", json);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Only one output format can be specified at a time.");
        File.Exists(xml).Should().BeFalse();
        File.Exists(json).Should().BeFalse();
    }

    [Fact]
    public void Run_WithQuietAndAReportFile_PrintsNothing()
    {
        // Arrange
        using TempDir dir = new();
        string report = dir.Combine("report.xml");

        // Act
        ToolRunner.Result result = ToolRunner.Run(Minimal, "--quiet", "-out", report);

        // Assert
        result.StdOut.Should().BeEmpty();
        File.Exists(report).Should().BeTrue();
    }

    [Fact]
    public void Run_WithMaxOfEachMessage_LimitsTheLocationsOfTheJsonReport()
    {
        // Arrange
        using TempDir dir = new();
        string report = dir.Combine("report.json");
        string book = dir.Combine("messages-maxcount");
        CopyFolder(Path.Combine(CorpusFiles.Root, "epubcheck", "reporting", "files", "messages-maxcount"), book);

        // Act
        ToolRunner.Run("-mode", "exp", book, "--json", report, "--maxOfEachMessage", "3");

        // Assert
        JsonNode message = JsonNode.Parse(File.ReadAllText(report))!["messages"]!.AsArray().First()!;
        message["locations"]!.AsArray().Should().HaveCount(3);
        message["additionalLocations"]!.GetValue<int>().Should().Be(27);
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
