using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core;
using Xunit;

namespace Signet.EpubCheck.Cli.Tests;

public sealed class EpubCheckToolTests
{
    private const string Completed = "EPUBCheck completed\n";

    [Fact]
    public void Run_WithoutArguments_Fails_AndSaysSo()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run();

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("At least one argument expected\n");
        result.StdOut.Should().Be(Completed);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("--help")]
    [InlineData("-help")]
    public void Run_WithHelp_PrintsTheHelpAndSucceeds(string option)
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run(option);

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().StartWith("EPUBCheck v" + EpubCheckVersion.Text + "\n");
        result.StdOut.Should().Contain("displays this help message");
        result.StdErr.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-version")]
    [InlineData("--version")]
    public void Run_WithVersion_PrintsTheVersionWithTheTimestamp(string option)
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run(option);

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Be($"EPUBCheck v{EpubCheckVersion.Text}\n");
        EpubCheckVersion.Text.Should().MatchRegex(@"^5\.4\.0-\d{12}$");
    }

    [Fact]
    public void Run_WithQuiet_PrintsNothingOfItsOwn()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-q", "--version");

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().BeEmpty();
    }

    [Fact]
    public void Run_WithAnUnknownOption_Fails_PrintsTheHelp_AndNamesTheOption()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("--unknown", "book.epub");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Unrecognized argument: \"--unknown\"\n");
        result.StdOut.Should().StartWith("EPUBCheck v").And.EndWith(Completed);
    }

    [Fact]
    public void Run_WithTwoFiles_RejectsTheSecond()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("a.epub", "b.epub");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Unrecognized argument: \"b.epub\"\n");
    }

    [Theory]
    [InlineData("-v", "Version number omitted from the -v argument.")]
    [InlineData("-mode", "Type omitted from the -mode argument.")]
    [InlineData("-profile", "Profile name omitted after the -profile argument.")]
    public void Run_WithAnOptionMissingItsArgument_SaysWhatIsMissing(string option, string message)
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.opf", option);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be(message + "\n");
        result.StdOut.Should().StartWith("-help displays help\n");
    }

    [Fact]
    public void Run_WithAnUnsupportedVersion_SaysSo()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-v", "4", "book.opf");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Version not supported\n");
    }

    [Fact]
    public void Run_WithAnUnknownProfile_NamesTheProfile()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-p", "nonsense", "missing.epub");

        // Assert
        result.StdErr.Should().StartWith("Unknown profile \"nonsense\", using default validation profile.\n");
    }

    [Theory]
    [InlineData("yes", "Argument \"yes\" to the --maxOfEachMessage option must be a number or 'unlimited'.")]
    public void Run_WithABadMaxOfEachMessage_Fails(string value, string message)
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "--maxOfEachMessage", value);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be(message + "\n");
    }

    [Fact]
    public void Run_WithoutTheValueOfMaxOfEachMessage_Fails()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "--maxOfEachMessage");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Argument to the --maxOfEachMessage option is missing.\n");
    }

    [Theory]
    [InlineData("1", "-1", "unlimited", "25")]
    public void Run_WithAValidMaxOfEachMessage_IsNotAnArgumentError(string a, string b, string c, string d)
    {
        // Act & Assert
        foreach (string value in new[] { a, b, c, d })
        {
            ToolRunner.Result result = ToolRunner.Run("-mode", "opf", "missing.opf", "--maxOfEachMessage", value);
            result.StdErr.Should().NotContain("maxOfEachMessage");
        }
    }

    [Fact]
    public void Run_WithALocaleThatLooksLikeAnOption_Fails()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "--locale", "-h");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().StartWith("Argument \"-h\" to the --locale option is incorrect.\n");
    }

    [Fact]
    public void Run_WithoutTheLocale_Fails()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "--locale");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().StartWith("Argument to the --locale option is missing.\n");
    }

    [Fact]
    public void Run_WithPolishLocale_SpeaksPolish()
    {
        // Act
        ToolRunner.Result result = ToolRunner.RunIn("en", "--locale", "pl", "-h");

        // Assert
        result.StdOut.Should().Contain("wyświetla ten komunikat pomocy");
    }

    [Theory]
    [InlineData("fr-FR", "en")]
    [InlineData("foobar", "en")]
    [InlineData("ar-EG", "pl")]
    public void Run_WithALanguageWithoutTexts_FallsBackToTheDefaultLocale(string requested, string defaultCulture)
    {
        // Act
        ToolRunner.Result result = ToolRunner.RunIn(defaultCulture, "--locale", requested, "-h");

        // Assert
        result.StdOut.Should().Contain(defaultCulture == "en" ? "displays this help message" : "wyświetla ten komunikat pomocy");
    }

    [Fact]
    public void Run_WithTheDefaultLocalePolish_SpeaksPolishWithoutAnOption()
    {
        // Act
        ToolRunner.Result result = ToolRunner.RunIn("pl", "--version");

        // Assert
        result.StdOut.Should().Be($"EPUBCheck v{EpubCheckVersion.Text}\n");
        ToolRunner.RunIn("pl").StdErr.Should().Be("Oczekiwano co najmniej jednego argumentu\n");
    }

    [Fact]
    public void Run_WithTwoReportFormats_RejectsThem()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "-o", "r.xml", "-j", "r.json");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Only one output format can be specified at a time.\n");
    }

    [Fact]
    public void Run_WithAJsonReportOfAMissingFile_NamesTheFile()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.epub", "-j", "-");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("File not found: \"book.epub\"\n");
    }

    [Fact]
    public void Run_OfANonEpubWithoutMode_AsksForTheMode()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("book.xhtml");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdOut.Should().StartWith("Mode required for non-epub files. Default version is 3.0.\n");
    }

    [Fact]
    public void Run_OfAnEpubWithAMode_IgnoresTheMode()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "opf", "missing.epub");

        // Assert
        result.StdErr.Should().StartWith("The mode and version arguments are ignored for epubs. They are retrieved from the files.\n");
        result.StdErr.Should().Contain("File not found: \"missing.epub\"\n");
    }

    [Fact]
    public void Run_OfAMissingFile_Fails_AndPrintsTheCounters()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "opf", "missing.opf");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("File not found: \"missing.opf\"\n");
        result.StdOut.Should().Be("Messages: 0 fatals / 0 errors / 0 warnings / 0 infos\n\nEPUBCheck completed\n");
    }

    [Fact]
    public void Run_WithTheUsageLevel_PrintsTheUsageCounterToo()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-u", "-mode", "opf", "missing.opf");

        // Assert
        result.StdOut.Should().Contain("0 infos / 0 usages\n");
    }

    [Fact]
    public void Run_WithAnErrorLevel_PrintsOnlyTheCountersOfThatLevelAndAbove()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-e", "-mode", "opf", "missing.opf");

        // Assert
        result.StdOut.Should().Be("Messages: 0 fatals / 0 errors\n\nEPUBCheck completed\n");
    }

    [Fact]
    public void Run_OfAModeThatDoesNotExistForTheVersion_Fails()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("nav.xhtml");
        File.WriteAllText(file, "x");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "nav", "-v", "2", file);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("The checker doesn’t validate type nav and version 2.0.1.\n");
        result.StdOut.Should().StartWith("-help displays help\n");
    }

    [Fact]
    public void Run_OfAFileInASingleDocumentMode_ChecksIt()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("book.opf");
        File.WriteAllText(file, "x");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "opf", file);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("FATAL(RSC-016)");
    }

    [Fact]
    public void Run_OfAnExpandedFolder_ChecksItAndLeavesNoEpubBehind()
    {
        // Arrange
        using TempDir dir = new();
        string folder = dir.Combine("book");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mimetype"), "application/epub+zip");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "exp", folder);

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("RSC-002");
        File.Exists(folder + ".epub").Should().BeFalse();
    }

    [Fact]
    public void Run_OfAnExpandedFolderWithSave_KeepsTheEpub()
    {
        // Arrange
        using TempDir dir = new();
        string folder = dir.Combine("book");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mimetype"), "application/epub+zip");

        // Act
        ToolRunner.Run("-mode", "exp", folder, "--save");

        // Assert
        File.Exists(folder + ".epub").Should().BeTrue();
    }

    [Fact]
    public void Run_OfAMissingExpandedDirectory_NamesTheDirectory()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-mode", "exp", "nowhere");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Be("Directory not found: \"nowhere\"\n");
    }

    [Fact]
    public void ListChecks_OnTheConsole_WritesTheHeaderAndEveryMessage()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("--listChecks");

        // Assert
        result.ExitCode.Should().Be(0);
        string[] lines = result.StdOut.Split('\n');
        lines[0].Should().Be("ID\tSeverity\tMessage\tSuggestion");
        lines.Should().HaveCount(1 + 323 + 1);
        result.StdOut.Should().Contain("\nOPF-014\tERROR\tThe property \"%1$s\" should be declared in the OPF file.\t\n");
    }

    [Fact]
    public void ListChecks_IntoAFile_WritesTheFileAndNotTheConsole()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("checks.tsv");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-l", file);

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().BeEmpty();
        File.ReadAllLines(file).Should().HaveCount(324);
        File.ReadAllText(file).Should().NotContain("\r");
    }

    [Fact]
    public void ListChecks_WithACustomMessagesFile_ShowsTheOverrides()
    {
        // Arrange
        using TempDir dir = new();
        string custom = dir.Combine("custom.tsv");
        File.WriteAllText(custom, "OPF-014\tWARNING\tMy text about %1$s\n");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-l", "-c", custom);

        // Assert
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\nOPF-014\tWARNING\t (severity overridden from ERROR) My text about %1$s\t\n");
    }

    [Fact]
    public void ListChecks_InPolish_ListsThePolishTexts()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-l", "--locale", "pl");

        // Assert
        result.StdOut.Should().Contain("\nRSC-001\tERROR\tNie można znaleźć pliku \"%1$s\".\t\n");
    }

    [Fact]
    public void CustomMessages_OfAMissingFile_ReportsChk001_OnTheErrorStream()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-l", "-c", "no-such-file.tsv");

        // Assert
        result.StdErr.Should().StartWith("ERROR(CHK-001): none/").And.Contain("no-such-file.tsv(-1,-1): The custom message overrides file was not found.\n");
    }

    [Fact]
    public void CustomMessages_WithABadLine_ReportsTheRealLine()
    {
        // Arrange
        using TempDir dir = new();
        string custom = dir.Combine("custom.tsv");
        File.WriteAllText(custom, "ID\tSeverity\nOPF-014\tERROR\nXXX-001\tERROR\n");

        // Act
        ToolRunner.Result result = ToolRunner.Run("-l", "-c", custom);

        // Assert
        result.StdErr.Should().Contain("custom.tsv(3,0): Unrecognized custom message id XXX-001");
    }

    [Fact]
    public void CustomMessages_NamedNone_SwitchesTheOverridesOff()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-c", "none", "-l");

        // Assert
        result.StdErr.Should().BeEmpty();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void CustomMessages_FollowedByAnOption_IsRejected()
    {
        // Act
        ToolRunner.Result result = ToolRunner.Run("-c", "-l");

        // Assert
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().StartWith("Expected the Custom message file name, but found \"-l\"\n");
    }

    [Fact]
    public void ToolMessages_InEnglishAndPolish_HaveTheSameKeysAndPlaceholders()
    {
        // Arrange
        var manager = new System.Resources.ResourceManager("Signet.EpubCheck.Cli.ToolMessages", typeof(Signet.EpubCheck.Cli.EpubCheckTool).Assembly);
        var port = new System.Resources.ResourceManager("Signet.EpubCheck.Cli.CliPortMessages", typeof(Signet.EpubCheck.Cli.EpubCheckTool).Assembly);

        // Act & Assert
        foreach (System.Resources.ResourceManager m in new[] { manager, port })
        {
            var english = m.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, false)!.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
            var polish = m.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("pl"), true, false)!.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
            polish.Keys.Order().Should().Equal(english.Keys.Order());
            foreach (string key in english.Keys)
            {
                static string Placeholders(string t) => string.Join(',', System.Text.RegularExpressions.Regex.Matches(t, @"%\d+\$[sd]").Select(x => x.Value).Order());
                Placeholders(polish[key]).Should().Be(Placeholders(english[key]), key);
            }
        }
    }
}
