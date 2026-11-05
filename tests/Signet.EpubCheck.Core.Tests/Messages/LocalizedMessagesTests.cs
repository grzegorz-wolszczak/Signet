using System.Collections;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class LocalizedMessagesTests
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static Hashtable Resources(CultureInfo culture) => Resources(LocalizedMessages.Manager, culture);

    private static Hashtable Resources(ResourceManager manager, CultureInfo culture)
    {
        ResourceSet set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().Aggregate(new Hashtable(), (table, e) =>
        {
            table.Add(e.Key, e.Value);
            return table;
        });
    }

    [Fact]
    public void ThePolishTexts_HaveTheKeysAndPlaceholdersOfTheEnglishOnes()
    {
        // Arrange
        Hashtable english = Resources(CultureInfo.InvariantCulture);
        Hashtable polish = Resources(Polish);

        // Act
        string[] missing = english.Keys.Cast<string>().Except(polish.Keys.Cast<string>()).Order().ToArray();
        string[] extra = polish.Keys.Cast<string>().Except(english.Keys.Cast<string>()).Order().ToArray();
        string[] differentPlaceholders = english.Keys.Cast<string>()
            .Where(polish.ContainsKey)
            .Where(key => Placeholders((string)english[key]!) != Placeholders((string)polish[key]!))
            .Order().ToArray();

        // Assert
        missing.Should().BeEmpty();
        extra.Should().BeEmpty();
        differentPlaceholders.Should().BeEmpty();
        english.Count.Should().Be(280);
    }

    private static string Placeholders(string text) =>
        string.Join(',', Regex.Matches(text, @"%\d+\$s").Select(m => m.Value).Order());

    [Fact]
    public void GetMessage_InEnglish_GivesTheTextsOfUpstream()
    {
        // Act
        Message message = LocalizedMessages.GetInstance(English).GetMessage(MessageId.OPF_014);

        // Assert
        message.Text.Should().Be("The property \"%1$s\" should be declared in the OPF file.");
        message.Severity.Should().Be(Severity.Error);
        message.OriginalSeverity.Should().Be(Severity.Error);
        message.Suggestion.Should().BeEmpty();
        message.Id.Should().Be(MessageId.OPF_014);
    }

    [Fact]
    public void GetMessage_InPolish_GivesPolishText()
    {
        // Act
        Message message = LocalizedMessages.GetInstance(Polish).GetMessage(MessageId.OPF_014);

        // Assert
        message.Text.Should().StartWith("Właściwość");
        message.Text.Should().Contain("%1$s");
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr-CA")]
    [InlineData("")]
    public void GetInstance_OfALanguageWithoutTexts_GivesEnglish(string name)
    {
        // Act
        LocalizedMessages messages = LocalizedMessages.GetInstance(CultureInfo.GetCultureInfo(name));

        // Assert
        messages.Language.Should().BeOneOf("en", "iv");
        messages.GetMessage(MessageId.OPF_014).Text.Should().StartWith("The property");
    }

    [Fact]
    public void GetInstance_OfPolishRegions_GivesPolish()
    {
        // Act & Assert
        LocalizedMessages.GetInstance(CultureInfo.GetCultureInfo("pl-PL")).Language.Should().Be("pl");
    }

    [Fact]
    public void GetMessage_OfIdsWithoutAText_GivesAnEmptyText_LikeUpstream()
    {
        // Act
        string[] texts = MessageIds.All.Select(id => LocalizedMessages.GetInstance(English).GetMessage(id).Text).ToArray();

        // Assert
        texts.Count(t => t.Length == 0).Should().Be(77);
        LocalizedMessages.GetInstance(English).GetMessage(MessageId.ACC_001).Text.Should().BeEmpty();
    }

    [Fact]
    public void ThePortsOwnTexts_ExistInBothLanguagesWithTheSamePlaceholders()
    {
        // Act
        Hashtable english = Resources(LocalizedMessages.PortManager, CultureInfo.InvariantCulture);
        Hashtable polish = Resources(LocalizedMessages.PortManager, Polish);

        // Assert
        polish.Keys.Cast<string>().Order().Should().Equal(english.Keys.Cast<string>().Order());
        english.Keys.Cast<string>().Should().Contain("NoDescription");
        Placeholders((string)polish["NoDescription"]!).Should().Be(Placeholders((string)english["NoDescription"]!));
    }

    [Fact]
    public void Format_OfAMessageWithoutAText_SaysThereIsNoDescription()
    {
        // Arrange
        LocalizedMessages english = LocalizedMessages.GetInstance(English);
        LocalizedMessages polish = LocalizedMessages.GetInstance(Polish);

        // Act & Assert
        english.Format(english.GetMessage(MessageId.ACC_001)).Should().Be("No description is available for ACC-001.");
        polish.Format(polish.GetMessage(MessageId.ACC_001)).Should().Be("Brak opisu komunikatu ACC-001.");
        english.GetMessage(MessageId.ACC_001).Text.Should().BeEmpty();
    }

    [Theory, AutoData]
    public void Format_FillsTheArguments(string property)
    {
        // Arrange
        LocalizedMessages messages = LocalizedMessages.GetInstance(English);

        // Act
        string text = messages.Format(messages.GetMessage(MessageId.OPF_014), property);

        // Assert
        text.Should().Be($"The property \"{property}\" should be declared in the OPF file.");
    }

    [Theory]
    [InlineData("element", "div", "Usage of the div element is outdated.")]
    [InlineData("ncx", null, "Usage of OPF 2 NCX navigation is outdated.")]
    [InlineData("font-obfuscation", null, "Usage of font obfuscation is outdated.")]
    [InlineData("property", "rendition:flow", "Usage of the \"rendition:flow\" property is outdated.")]
    [InlineData("content-fallback", "a.png", "Usage of manifest content fallback (for resource \"a.png\") is outdated.")]
    [InlineData("something else", null, "Usage of something else is outdated.")]
    public void Format_ReplacesALocalizedArgument_AndFillsItWithTheOnesAfterIt(string argument, string? following, string expected)
    {
        // Arrange
        LocalizedMessages messages = LocalizedMessages.GetInstance(English);
        object?[] args = following is null ? [argument] : [argument, following];

        // Act
        string text = messages.Format(messages.GetMessage(MessageId.OBS_001), args);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void Format_OfTheMimetypeMessage_KeepsTheSpaceBeforeTheLocalizedWords()
    {
        // Arrange
        LocalizedMessages messages = LocalizedMessages.GetInstance(English);

        // Act
        string text = messages.Format(messages.GetMessage(MessageId.PKG_007), "whitespace");

        // Assert
        text.Should().Be("The content of the mimetype file must be the string \"application/epub+zip\" with no whitespace or newline.");
    }

    [Fact]
    public void Format_OfATextItCannotFill_AppendsTheReason()
    {
        // Arrange
        LocalizedMessages messages = LocalizedMessages.GetInstance(English);

        // Act
        string text = messages.Format(messages.GetMessage(MessageId.CHK_002), "only one");

        // Assert
        text.Should().StartWith(messages.GetMessage(MessageId.CHK_002).Text + " !!! ");
    }

    [Theory]
    [InlineData("onix-record", "the \"record\" keyword with the properties attribute value \"onix\"")]
    [InlineData("unknown", "a custom prefixed property")]
    [InlineData(null, "a custom prefixed property")]
    public void GetSuggestion_WithAKey_FallsBackToTheDefaultOne(string? key, string expected)
    {
        // Act & Assert
        LocalizedMessages.GetInstance(English).GetSuggestion(MessageId.OPF_086, key).Should().Be(expected);
    }

    [Fact]
    public void GetSuggestion_Plain_And_OfAMessageWithout()
    {
        // Arrange
        LocalizedMessages messages = LocalizedMessages.GetInstance(English);

        // Act & Assert
        messages.GetSuggestion(MessageId.OPF_035).Should().Be("Use \"application/xhtml+xml\" instead.");
        messages.GetSuggestion(MessageId.CHK_001).Should().BeEmpty();
        messages.GetSuggestion(MessageId.CHK_001, "key").Should().BeEmpty();
        messages.GetSuggestion(MessageId.NAV_011, "spine").Should().Be("spine");
        LocalizedMessages.GetInstance(Polish).GetSuggestion(MessageId.NAV_011, "document").Should().Be("dokumentu");
    }

    [Fact]
    public void LocalizedMessageDictionary_GivesTheMessagesOfItsLanguage()
    {
        // Arrange
        LocalizedMessageDictionary dictionary = new(Polish);

        // Act
        Message message = dictionary.GetMessage(MessageId.RSC_001);

        // Assert
        dictionary.Language.Should().Be("pl");
        dictionary.Format(message, "a.xhtml").Should().Be("Nie można znaleźć pliku \"a.xhtml\".");
    }
}
