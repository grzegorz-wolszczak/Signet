using System;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontFaceAddition"/> — writing <c>@font-face</c> rules into a stylesheet.</summary>
public sealed class FontFaceAdditionTests
{
    private const string CssPath = "OEBPS/Styles/style.css";

    private static readonly FontFaceRequest Regular = new("OEBPS/Fonts/K-Regular.ttf", "KCaecilia", "normal", "normal");
    private static readonly FontFaceRequest Bold = new("OEBPS/Fonts/K-Bold.ttf", "KCaecilia", "bold", "normal");

    [Fact]
    public void Apply_puts_the_rules_at_the_top_with_urls_relative_to_the_stylesheet()
    {
        string result = FontFaceAddition.Apply("p {\n    margin: 0;\n}\n", CssPath, new[] { Regular, Bold });

        result.Should().Be(
            "@font-face {\n    font-family: \"KCaecilia\";\n    font-style: normal;\n    font-weight: normal;\n    src: url(\"../Fonts/K-Regular.ttf\");\n}\n\n"
            + "@font-face {\n    font-family: \"KCaecilia\";\n    font-style: normal;\n    font-weight: bold;\n    src: url(\"../Fonts/K-Bold.ttf\");\n}\n\n"
            + "p {\n    margin: 0;\n}\n");
    }

    [Fact]
    public void Apply_inserts_after_the_existing_font_face_rules_and_after_imports()
    {
        const string withFace = "@import url(x.css);\n\n@font-face { font-family: A; src: url(a.ttf) }\n\np { margin: 0 }\n";
        const string withImport = "@charset \"utf-8\";\n@import url(x.css);\np { margin: 0 }";

        FontFaceAddition.Apply(withFace, CssPath, new[] { Regular })
            .Should().StartWith("@import url(x.css);\n\n@font-face { font-family: A; src: url(a.ttf) }\n\n@font-face {\n  font-family: \"KCaecilia\";")
            .And.EndWith("}\n\np { margin: 0 }\n");
        FontFaceAddition.Apply(withImport, CssPath, new[] { Regular })
            .Should().StartWith("@charset \"utf-8\";\n@import url(x.css);\n\n@font-face {")
            .And.EndWith("}\n\np { margin: 0 }");
    }

    [Fact]
    public void Apply_sets_the_family_of_an_existing_body_rule()
    {
        const string css = "body {\n  margin: 0;\n  font-family: Georgia !important;\n}\n";

        string result = FontFaceAddition.Apply(css, CssPath, Array.Empty<FontFaceRequest>(), "KCaecilia", "serif");

        result.Should().Be("body {\n  margin: 0;\n  font-family: \"KCaecilia\", serif !important;\n}\n");
    }

    [Theory]
    [InlineData("body {\n  margin: 0\n}\n", "body {\n  margin: 0;\n  font-family: \"KCaecilia\", serif;\n}\n")]
    [InlineData("body { margin: 0 }", "body { margin: 0; font-family: \"KCaecilia\", serif; }")]
    [InlineData("body {}", "body { font-family: \"KCaecilia\", serif; }")]
    public void Apply_adds_the_family_to_a_body_rule_without_one(string css, string expected)
    {
        FontFaceAddition.Apply(css, CssPath, Array.Empty<FontFaceRequest>(), "KCaecilia", "serif").Should().Be(expected);
    }

    [Fact]
    public void Apply_adds_a_body_rule_after_the_new_rules_when_there_is_none()
    {
        string result = FontFaceAddition.Apply("p { margin: 0 }\n", CssPath, new[] { Regular }, "KCaecilia", "sans-serif");

        result.Should().Contain("}\n\nbody {\n  font-family: \"KCaecilia\", sans-serif;\n}\n\np { margin: 0 }\n");
    }

    [Fact]
    public void FindConflict_detects_the_same_file_and_the_same_face()
    {
        BookFontIndex index = BookFontIndex.Build(new[]
        {
            new FontIndexSource(CssPath, "@font-face { font-family: 'kcaecilia'; font-weight: 400; src: url(../Fonts/K-Old.ttf) }", true),
        });

        FontFaceAddition.FindConflict(index, Regular, Array.Empty<FontFaceRequest>()).Kind.Should().Be(FontFaceConflictKind.SameFace);
        FontFaceAddition.FindConflict(index, Bold, Array.Empty<FontFaceRequest>()).Kind.Should().Be(FontFaceConflictKind.None);
        FontFaceAddition.FindConflict(index, Bold with { FontBookPath = "OEBPS/Fonts/K-Old.ttf" }, Array.Empty<FontFaceRequest>())
            .Kind.Should().Be(FontFaceConflictKind.SameFile);

        FontFaceConflict inBatch = FontFaceAddition.FindConflict(index, Bold with { FontBookPath = "OEBPS/Fonts/K-Bold2.ttf" }, new[] { Bold });
        inBatch.Kind.Should().Be(FontFaceConflictKind.SameFace);
        inBatch.EarlierFontBookPath.Should().Be(Bold.FontBookPath);
    }

    [Theory]
    [InlineData(false, "a.woff", FontFormatWarning.NotInEpub2)]
    [InlineData(false, "a.woff2", FontFormatWarning.NotInEpub2)]
    [InlineData(true, "a.woff", FontFormatWarning.None)]
    [InlineData(true, "a.woff2", FontFormatWarning.Woff2)]
    [InlineData(false, "a.ttf", FontFormatWarning.None)]
    public void Describe_warns_about_formats_the_book_version_does_not_guarantee(bool isEpub3, string file, FontFormatWarning expected)
    {
        FontFaceAddition.Describe("OEBPS/Fonts/" + file, FontFileInfo.Empty, isEpub3).FormatWarning.Should().Be(expected);
    }

    [Fact]
    public void Describe_flags_restricted_embedding()
    {
        FontFileInfo info = OpenTypeFontInfo.Parse(FontFileBuilder.Ttf("Acme", "Regular", 400, false, fsType: 0x0002));

        FontFaceCandidate candidate = FontFaceAddition.Describe("OEBPS/Fonts/acme.ttf", info, isEpub3: true);

        candidate.EmbeddingRestricted.Should().BeTrue();
        candidate.Suggested.Should().Be(new FontFaceDescriptor("Acme", "normal", "normal"));
    }
}
