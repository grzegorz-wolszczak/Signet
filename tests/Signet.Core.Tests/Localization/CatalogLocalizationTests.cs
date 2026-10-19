using System;
using System.Globalization;
using AwesomeAssertions;
using System.Linq;
using Signet.Core.MainUI;
using Signet.Core.Metadata;
using Signet.Core.Semantics;
using Xunit;

namespace Signet.Core.Tests.Localization;

/// <summary>
/// Reference catalogs in the UI language (<c>CatalogText</c>): names/descriptions for the UI are translated,
/// titles inserted into the book are in English, searching by name works in both variants.
/// </summary>
public sealed class CatalogLocalizationTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public CatalogLocalizationTests() => CultureInfo.CurrentUICulture = new CultureInfo("pl");

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;

    [Fact]
    public void Landmark_names_and_descriptions_follow_the_ui_language()
    {
        // Act & Assert
        Landmarks.GetName("bodymatter").Should().Be("Część główna");

        // Act & Assert
        Landmarks.GetCodeMap()["toc"].Name.Should().Be("Spis treści");

        // Act & Assert
        Landmarks.GetDescriptionByCode("appendix").Should().Be("Informacje uzupełniające.");

        // Act
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        // Act & Assert
        Landmarks.GetName("bodymatter").Should().Be("Body Matter");

        // Act & Assert
        Landmarks.GetDescriptionByCode("appendix").Should().Be("Supplemental information.");
    }

    [Fact]
    public void Titles_inserted_into_the_book_stay_english()
    {
        // Act & Assert
        Landmarks.GetTitle("toc", "pl").Should().Be("Table of Contents");

        // Act & Assert
        GuideItems.GetTitle("toc", "pl").Should().Be("Table of Contents");
    }

    [Theory]
    [InlineData("Spis treści")]
    [InlineData("Table of Contents")]
    public void Lookup_by_name_accepts_translated_and_english_names(string name)
    {
        // Act & Assert
        Landmarks.GetCode(name).Should().Be("toc");

        // Act & Assert
        GuideItems.GetCode(name).Should().Be("toc");

        // Act & Assert
        Landmarks.IsLandmarksName(name).Should().BeTrue();

        // Act & Assert
        GuideItems.GetDescriptionByName(name).Should().Be(GuideItems.GetDescriptionByCode("toc"));
    }

    [Fact]
    public void Marc_relators_are_translated_and_found_by_polish_name()
    {
        // Act & Assert
        MarcRelators.GetName("aut").Should().Be("Autor");

        // Act & Assert
        MarcRelators.GetName("edt").Should().Be("Redaktor");

        // Act & Assert
        MarcRelators.GetCode("Redaktor").Should().Be("edt");

        // Act & Assert
        MarcRelators.GetCode("Editor").Should().Be("edt");

        // Act & Assert
        MarcRelators.IsRelatorName("Ilustrator").Should().BeTrue();

        // Act & Assert
        MarcRelators.GetCodeMap()["trl"].Name.Should().Be("Tłumacz");
    }

    [Fact]
    public void Language_names_are_translated_including_regional_variants()
    {
        // Act & Assert
        Language.GetLanguageName("en").Should().Be("Angielski");

        // Act & Assert
        Language.GetLanguageName("ar-DZ").Should().Be("Arabski - Algieria");

        // Act & Assert
        Language.GetLanguageName("gd").Should().Be("Gaelicki szkocki");

        // Act & Assert
        Language.GetLanguageCode("Francuski").Should().Be("fr");

        // Act & Assert
        Language.GetLanguageCode("French").Should().Be("fr");

        // Act & Assert
        Language.GetSortedPrimaryLanguageNames().Should().Contain("Polski");
    }

    [Fact]
    public void Metadata_field_names_are_translated()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub3Elements["dc:creator-aut"].Name.Should().Be("Autor");

        // Act & Assert
        MetadataFieldCatalog.Epub3Properties["file-as"].Name.Should().Be("Sortuj jako");

        // Act & Assert
        MetadataFieldCatalog.Epub2Elements["dc:title"].Description
            .Should().Be("Główny tytuł publikacji EPUB. Może istnieć tylko jeden tytuł.");
    }

    [Fact]
    public void Aria_roles_and_clips_are_translated_but_clip_templates_are_not()
    {
        // Act & Assert
        AriaRoles.GetName("doc-chapter").Should().Be("Rozdział");

        // Act & Assert
        AriaRoles.GetCode("Rozdział").Should().Be("doc-chapter");

        // Act & Assert
        AriaClips.GetName("chapter").Should().Be("Sekcja: rozdział");

        // Act & Assert
        AriaClips.GetDescriptionByCode("chapter").Should().StartWith("<section");
    }

    [Fact]
    public void Special_characters_and_unicode_blocks_have_translated_display_texts()
    {
        // Act
        SpecialCharacterEntry nbsp = SpecialCharacters.Entries.First(e => e.Entity == "&nbsp;");

        // Assert
        nbsp.Code.Should().Be("U+00A0");
        nbsp.Description.Should().Be("non-breaking space");
        nbsp.DisplayDescription.Should().Be("twarda spacja (niełamliwa)");

        // Act
        UnicodeBlock greek = UnicodeBlocks.Blocks.First(b => b.Start == 0x0370);

        // Assert
        greek.Name.Should().Be("Greek and Coptic");
        greek.DisplayName.Should().Be("Grecki i koptyjski");
    }
}
