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
        Landmarks.GetName("bodymatter").Should().Be("Część główna");
        Landmarks.GetCodeMap()["toc"].Name.Should().Be("Spis treści");
        Landmarks.GetDescriptionByCode("appendix").Should().Be("Informacje uzupełniające.");

        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Landmarks.GetName("bodymatter").Should().Be("Body Matter");
        Landmarks.GetDescriptionByCode("appendix").Should().Be("Supplemental information.");
    }

    [Fact]
    public void Titles_inserted_into_the_book_stay_english()
    {
        Landmarks.GetTitle("toc", "pl").Should().Be("Table of Contents");
        GuideItems.GetTitle("toc", "pl").Should().Be("Table of Contents");
    }

    [Theory]
    [InlineData("Spis treści")]
    [InlineData("Table of Contents")]
    public void Lookup_by_name_accepts_translated_and_english_names(string name)
    {
        Landmarks.GetCode(name).Should().Be("toc");
        GuideItems.GetCode(name).Should().Be("toc");
        Landmarks.IsLandmarksName(name).Should().BeTrue();
        GuideItems.GetDescriptionByName(name).Should().Be(GuideItems.GetDescriptionByCode("toc"));
    }

    [Fact]
    public void Marc_relators_are_translated_and_found_by_polish_name()
    {
        MarcRelators.GetName("aut").Should().Be("Autor");
        MarcRelators.GetName("edt").Should().Be("Redaktor");
        MarcRelators.GetCode("Redaktor").Should().Be("edt");
        MarcRelators.GetCode("Editor").Should().Be("edt");
        MarcRelators.IsRelatorName("Ilustrator").Should().BeTrue();
        MarcRelators.GetCodeMap()["trl"].Name.Should().Be("Tłumacz");
    }

    [Fact]
    public void Language_names_are_translated_including_regional_variants()
    {
        Language.GetLanguageName("en").Should().Be("Angielski");
        Language.GetLanguageName("ar-DZ").Should().Be("Arabski - Algieria");
        Language.GetLanguageName("gd").Should().Be("Gaelicki szkocki");
        Language.GetLanguageCode("Francuski").Should().Be("fr");
        Language.GetLanguageCode("French").Should().Be("fr");
        Language.GetSortedPrimaryLanguageNames().Should().Contain("Polski");
    }

    [Fact]
    public void Metadata_field_names_are_translated()
    {
        MetadataFieldCatalog.Epub3Elements["dc:creator-aut"].Name.Should().Be("Autor");
        MetadataFieldCatalog.Epub3Properties["file-as"].Name.Should().Be("Sortuj jako");
        MetadataFieldCatalog.Epub2Elements["dc:title"].Description
            .Should().Be("Główny tytuł publikacji EPUB. Może istnieć tylko jeden tytuł.");
    }

    [Fact]
    public void Aria_roles_and_clips_are_translated_but_clip_templates_are_not()
    {
        AriaRoles.GetName("doc-chapter").Should().Be("Rozdział");
        AriaRoles.GetCode("Rozdział").Should().Be("doc-chapter");
        AriaClips.GetName("chapter").Should().Be("Sekcja: rozdział");
        AriaClips.GetDescriptionByCode("chapter").Should().StartWith("<section");
    }

    [Fact]
    public void Special_characters_and_unicode_blocks_have_translated_display_texts()
    {
        SpecialCharacterEntry nbsp = SpecialCharacters.Entries.First(e => e.Entity == "&nbsp;");
        nbsp.Code.Should().Be("U+00A0");
        nbsp.Description.Should().Be("non-breaking space");
        nbsp.DisplayDescription.Should().Be("twarda spacja (niełamliwa)");

        UnicodeBlock greek = UnicodeBlocks.Blocks.First(b => b.Start == 0x0370);
        greek.Name.Should().Be("Greek and Coptic");
        greek.DisplayName.Should().Be("Grecki i koptyjski");
    }
}
