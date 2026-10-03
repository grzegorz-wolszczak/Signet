using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="NcxResource"/> — the default template and identifier replacement.</summary>
public sealed class NcxResourceTests
{
    private static NcxResource Make(out TempDir root)
    {
        root = new TempDir();
        return new NcxResource(root.Path, root.Combine("OEBPS", "toc.ncx"));
    }

    [Fact]
    public void FillWithDefaultText_epub2_has_doctype_and_relative_content_href()
    {
        NcxResource ncx = Make(out TempDir root);
        using (root)
        {
            ncx.FillWithDefaultText("2.0", "OEBPS/Text/Section0001.xhtml");

            string text = ncx.GetText();
            text.Should().Contain("<!DOCTYPE ncx");
            text.Should().Contain("<content src=\"Text/Section0001.xhtml\" />");
            text.Should().Contain("<text>Start</text>");
            ncx.IsWellFormed().Should().BeTrue();
        }
    }

    [Fact]
    public void FillWithDefaultText_epub3_has_no_doctype()
    {
        NcxResource ncx = Make(out TempDir root);
        using (root)
        {
            ncx.FillWithDefaultText("3.0", "OEBPS/Text/Section0001.xhtml");

            ncx.GetText().Should().NotContain("<!DOCTYPE");
        }
    }

    [Fact]
    public void GetNcxDocument_and_SetNcxDocument_round_trip_through_resource()
    {
        NcxResource ncx = Make(out TempDir root);
        using (root)
        {
            ncx.FillWithDefaultText("2.0", "OEBPS/Text/Section0001.xhtml");

            NcxDocument document = ncx.GetNcxDocument();
            document.NavMap.Should().ContainSingle();
            document.NavMap[0].Label = "Rozdzial 1";
            ncx.SetNcxDocument(document);

            ncx.GetText().Should().Contain("<text>Rozdzial 1</text>");
            ncx.GetNcxDocument().NavMap[0].Label.Should().Be("Rozdzial 1");
            ncx.IsWellFormed().Should().BeTrue();
        }
    }

    [Fact]
    public void SetMainId_replaces_the_placeholder()
    {
        NcxResource ncx = Make(out TempDir root);
        using (root)
        {
            ncx.FillWithDefaultText("2.0", "OEBPS/Text/Section0001.xhtml");
            ncx.SetMainId("urn:uuid:1234");

            ncx.GetText().Should().NotContain("ID_UNKNOWN");
            ncx.GetText().Should().Contain("content=\"urn:uuid:1234\"");
        }
    }
}
