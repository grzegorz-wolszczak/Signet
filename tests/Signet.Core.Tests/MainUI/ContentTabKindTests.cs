using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="ContentTabKindMap"/> — choosing the tab kind per resource type.</summary>
public sealed class ContentTabKindTests
{
    [Theory]
    [InlineData(ResourceType.Html, ContentTabKind.Flow)]
    [InlineData(ResourceType.Css, ContentTabKind.Css)]
    [InlineData(ResourceType.Image, ContentTabKind.Image)]
    [InlineData(ResourceType.Svg, ContentTabKind.Svg)]
    [InlineData(ResourceType.MiscText, ContentTabKind.MiscText)]
    [InlineData(ResourceType.Xml, ContentTabKind.Xml)]
    [InlineData(ResourceType.Opf, ContentTabKind.Opf)]
    [InlineData(ResourceType.Ncx, ContentTabKind.Ncx)]
    [InlineData(ResourceType.Text, ContentTabKind.Text)]
    [InlineData(ResourceType.Audio, ContentTabKind.AudioVideo)]
    [InlineData(ResourceType.Video, ContentTabKind.AudioVideo)]
    [InlineData(ResourceType.Pdf, ContentTabKind.Pdf)]
    [InlineData(ResourceType.Font, ContentTabKind.Font)]
    [InlineData(ResourceType.Generic, ContentTabKind.Unsupported)]
    public void Each_resource_type_maps_to_the_matching_tab_kind(ResourceType type, ContentTabKind expected)
    {
        ContentTabKindMap.ForType(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(ContentTabKind.Flow, true)]
    [InlineData(ContentTabKind.Css, true)]
    [InlineData(ContentTabKind.Xml, true)]
    [InlineData(ContentTabKind.Text, true)]
    [InlineData(ContentTabKind.Image, false)]
    [InlineData(ContentTabKind.Font, false)]
    [InlineData(ContentTabKind.AudioVideo, false)]
    [InlineData(ContentTabKind.Pdf, false)]
    [InlineData(ContentTabKind.Unsupported, false)]
    public void Editable_kinds_are_the_text_backed_ones(ContentTabKind kind, bool editable)
    {
        ContentTabKindMap.IsEditable(kind).Should().Be(editable);
    }
}
