using System.IO;
using System.Linq;
using System.Xml.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// The XML settings file (<see cref="SettingsStore"/> over <c>SettingsFile</c>): values of every kind survive a save
/// and a reload, the file is the documented XML and a JSON settings file is not read.
/// </summary>
public sealed class SettingsXmlTests
{
    private static readonly Fixture Fixture = new();

    [Fact]
    public void Strings_numbers_booleans_lists_and_maps_survive_save_and_reload()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        string language = Fixture.Create<string>();
        string[] recent = { "/books/a b.epub", "C:\\Books\\<odd> & name.epub", "  " };
        SettingsStore sut = new(path);
        sut.UiLanguage = language;
        sut.ZoomText = 1.25f;
        sut.SpellCheck = true;
        sut.RecentFiles = recent;
        sut.SetStringMap("dock_panels", new System.Collections.Generic.Dictionary<string, string> { ["BookBrowser"] = "1", ["Preview"] = "0" });
        sut.SetWindowGeometry("main", new WindowGeometry(-8, 12, 1200, 800, Maximized: true, FullScreen: false));

        // Act
        sut.Save();

        SettingsStore reloaded = new(path);

        // Assert
        reloaded.UiLanguage.Should().Be(language);
        reloaded.ZoomText.Should().Be(1.25f);
        reloaded.SpellCheck.Should().BeTrue();
        reloaded.RecentFiles.Should().Equal(recent);
        reloaded.GetStringMap("dock_panels").Should().BeEquivalentTo(sut.GetStringMap("dock_panels"));
        reloaded.GetWindowGeometry("main").Should().Be(new WindowGeometry(-8, 12, 1200, 800, Maximized: true, FullScreen: false));
    }

    [Fact]
    public void The_file_is_xml_with_groups_and_typed_values()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore sut = new(path);
        sut.SpellCheck = true;
        sut.RecentFiles = new[] { "/books/a.epub" };

        // Act
        sut.Save();

        XElement root = XDocument.Load(path).Root!;

        // Assert
        root.Name.LocalName.Should().Be("settings");
        XElement[] values = root.Elements("group").SelectMany(g => g.Elements()).ToArray();
        values.Should().Contain(e => e.Name.LocalName == "bool" && e.Attribute("key")!.Value == "spell_check" && e.Value == "true");
        values.Single(e => e.Name.LocalName == "array" && e.Attribute("key")!.Value == "recentfiles")
            .Elements("string").Select(e => e.Value).Should().Equal("/books/a.epub");
    }

    [Fact]
    public void A_json_settings_file_is_not_read()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, "{ \"user_preferences\": { \"ui_language\": \"pl\" } }");

        // Act & Assert
        new SettingsStore(path).UiLanguage.Should().Be("en", "the default");
    }

    [Fact]
    public void Unreadable_values_are_skipped_like_missing_settings()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, """
            <settings>
              <group name="user_preferences">
                <number key="zoom_text">not a number</number>
                <bool key="spell_check">maybe</bool>
                <unknown key="ui_language">pl</unknown>
              </group>
            </settings>
            """);

        // Act
        SettingsStore sut = new(path);

        // Assert
        sut.ZoomText.Should().Be(1.0f);
        sut.SpellCheck.Should().BeFalse();
        sut.UiLanguage.Should().Be("en");
    }

    [Fact]
    public void Default_settings_file_is_settings_xml()
    {
        // Act & Assert
        AppDirectories.SettingsFileName.Should().Be("settings.xml");
    }
}
