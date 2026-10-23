using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AwesomeAssertions;
using Signet.Core.Footnotes;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>
/// Tests of <see cref="ElementIds"/> — the index that answers what AngleSharp's <c>GetElementById</c> answers,
/// without walking the document for every question.
/// </summary>
public sealed class ElementIdsTests
{
    public static TheoryData<string> AllCorpusXhtml()
    {
        string malformedDir = $"{Path.DirectorySeparatorChar}malformed{Path.DirectorySeparatorChar}";
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.xhtml", SearchOption.AllDirectories))
        {
            if (!path.Contains(malformedDir, StringComparison.Ordinal))
            {
                data.Add(path);
            }
        }

        return data;
    }

    private static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    /// <summary>
    /// The index has to answer exactly what <c>GetElementById</c> answers, for every id of a real file and for one
    /// that is not there. This is the guard for replacing the document walk with a lookup.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void The_index_answers_what_GetElementById_answers_for_every_id_of_a_file(string path)
    {
        // Arrange
        IDocument document = Parse(File.ReadAllText(path));
        List<string> ids = document.All
            .Select(e => e.GetAttribute("id"))
            .Where(id => id is { Length: > 0 })
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Act
        Dictionary<string, IElement> index = ElementIds.Of(document);

        // Assert
        index.Should().HaveCount(ids.Count);
        foreach (string id in ids)
        {
            index.GetValueOrDefault(id).Should().BeSameAs(document.GetElementById(id), "the element of id {0}", id);
        }

        index.GetValueOrDefault("no-such-id-anywhere").Should().BeNull();
    }

    /// <summary>An id written on two elements belongs to the first of them, as <c>GetElementById</c> has it.</summary>
    [Fact]
    public void A_repeated_id_belongs_to_the_first_element_that_carries_it()
    {
        // Arrange
        IDocument document = Parse("<html><body><p id=\"twin\">first</p><p id=\"twin\">second</p></body></html>");

        // Act
        Dictionary<string, IElement> index = ElementIds.Of(document);

        // Assert
        index["twin"].TextContent.Should().Be("first");
        index["twin"].Should().BeSameAs(document.GetElementById("twin"));
    }

    [Fact]
    public void An_empty_id_is_left_out()
    {
        // Arrange
        IDocument document = Parse("<html><body><p id=\"\">a</p><p id=\"real\">b</p></body></html>");

        // Act
        Dictionary<string, IElement> index = ElementIds.Of(document);

        // Assert
        index.Keys.Should().Equal("real");
    }
}
