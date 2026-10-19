using AwesomeAssertions;
using Signet.Core.Fonts;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontFaceDescriptors"/> — the <c>@font-face</c> descriptors derived from a font file.</summary>
public sealed class FontFaceDescriptorsTests
{
    [Theory]
    [InlineData(400, false, "Regular", "normal", "normal")]
    [InlineData(400, true, "Italic", "normal", "italic")]
    [InlineData(700, false, "Bold", "bold", "normal")]
    [InlineData(700, true, "Bold Italic", "bold", "italic")]
    [InlineData(300, false, "Light", "300", "normal")]
    [InlineData(600, true, "SemiBold Italic", "600", "italic")]
    public void FromFont_uses_the_weight_class_and_the_italic_flag(int weight, bool italic, string subfamily, string cssWeight, string cssStyle)
    {
        // Arrange
        FontFileInfo info = OpenTypeFontInfo.Parse(FontFileBuilder.Ttf("KCaecilia Condensed", subfamily, (ushort)weight, italic));

        // Act
        FontFaceDescriptor descriptor = FontFaceDescriptors.FromFont(info, "CaeciliaCondensed-Whatever.ttf");

        // Assert
        descriptor.Should().Be(new FontFaceDescriptor("KCaecilia Condensed", cssWeight, cssStyle));
    }

    [Fact]
    public void FromFont_prefers_the_typographic_family()
    {
        // Arrange
        FontFileInfo info = OpenTypeFontInfo.Parse(FontFileBuilder.Ttf("Acme Light", "Regular", 300, false, typographicFamily: "Acme"));

        // Act & Assert
        FontFaceDescriptors.FromFont(info, "Acme-Light.otf").Should().Be(new FontFaceDescriptor("Acme", "300", "normal"));
    }

    [Fact]
    public void FromFont_writes_a_weight_range_for_a_variable_font()
    {
        // Arrange
        FontFileInfo info = new FontFileInfo("Acme", "Regular", "Acme", "1.0") { WeightAxisMin = 100, WeightAxisMax = 900, WeightClass = 400 };

        // Act & Assert
        FontFaceDescriptors.FromFont(info, "Acme.ttf").Weight.Should().Be("100 900");
    }

    [Fact]
    public void FromFont_trusts_the_bold_flag_when_the_weight_class_says_regular()
    {
        // Arrange
        FontFileInfo info = new FontFileInfo("Acme", "Bold", "Acme Bold", "1.0") { WeightClass = 400, FsSelection = 0x0020 };

        // Act & Assert
        FontFaceDescriptors.FromFont(info, "Acme-Bold.ttf").Weight.Should().Be("bold");
    }

    [Theory]
    [InlineData("CaeciliaCondensed-BoldItalic.ttf", "CaeciliaCondensed", "bold", "italic")]
    [InlineData("Open_Sans-SemiBold.woff2", "Open Sans", "600", "normal")]
    [InlineData("Garamond.otf", "Garamond", "normal", "normal")]
    public void FromFont_falls_back_to_the_file_name_without_names(string fileName, string family, string weight, string style)
    {
        // Act & Assert
        FontFaceDescriptors.FromFont(FontFileInfo.Empty, fileName).Should().Be(new FontFaceDescriptor(family, weight, style));
    }

    [Theory]
    [InlineData((byte)2, (byte)11, (byte)3, (short)0, "sans-serif")]
    [InlineData((byte)2, (byte)2, (byte)9, (short)0, "monospace")]
    [InlineData((byte)3, (byte)0, (byte)0, (short)0, "cursive")]
    [InlineData((byte)0, (byte)0, (byte)0, (short)0x0800, "sans-serif")]
    [InlineData((byte)0, (byte)0, (byte)0, (short)0x0100, "serif")]
    [InlineData((byte)0, (byte)0, (byte)0, (short)0, "serif")]
    public void GenericFallback_follows_panose_and_the_family_class(byte familyType, byte serifStyle, byte proportion, short familyClass, string expected)
    {
        // Arrange
        FontFileInfo info = FontFileInfo.Empty with
        {
            PanoseFamilyType = familyType,
            PanoseSerifStyle = serifStyle,
            PanoseProportion = proportion,
            FamilyClass = familyClass,
        };

        // Act & Assert
        FontFaceDescriptors.GenericFallback(info).Should().Be(expected);
    }

    [Theory]
    [InlineData("normal", 400, 400)]
    [InlineData("bold", 700, 700)]
    [InlineData("300", 300, 300)]
    [InlineData("100 900", 100, 900)]
    [InlineData("", 400, 400)]
    [InlineData("bold !important", 700, 700)]
    public void ParseWeight_understands_keywords_numbers_and_ranges(string value, int min, int max)
    {
        // Act & Assert
        FontFaceDescriptors.ParseWeight(value).Should().Be(new FontWeightRange(min, max));
    }

    [Theory]
    [InlineData("\"KCaecilia\"", "kcaecilia")]
    [InlineData("'K  Caecilia '", "k caecilia")]
    [InlineData("K\\ Caecilia", "k caecilia")]
    public void FamilyKey_ignores_quotes_case_and_spacing(string family, string key)
    {
        // Act & Assert
        FontFaceDescriptors.FamilyKey(family).Should().Be(key);
    }

    [Theory]
    [InlineData(700, 700, "italic", "bold italic")]
    [InlineData(400, 400, "italic", "italic")]
    [InlineData(700, 700, "normal", "bold")]
    [InlineData(400, 400, "normal", "normal")]
    [InlineData(100, 900, "normal", "100 900")]
    public void Describe_names_the_face(int min, int max, string style, string expected)
    {
        // Act & Assert
        FontFaceDescriptors.Describe(new FontWeightRange(min, max), style).Should().Be(expected);
    }
}
