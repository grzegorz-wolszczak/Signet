using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// The memo of derivatives is kept between the documents a schema validates (and per thread): what a document is told must not depend on
/// the documents checked before it, nor on what other threads check at the same time.
/// </summary>
public sealed class RelaxNgSharedMemoTests
{
    private const string Ns = "http://relaxng.org/ns/structure/1.0";
    private const string SchemaUri = "file:///schemas/test.rng";
    private const string Schema =
        "<element name=\"doc\" xmlns=\"" + Ns + "\"><oneOrMore><choice>"
        + "<element name=\"a\"><empty/></element><element name=\"b\"><attribute name=\"x\"/><text/></element>"
        + "</choice></oneOrMore></element>";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Validate_TheSameDocumentsRepeatedly_GiveTheSameMessagesEveryTime()
    {
        // Arrange
        RelaxNgSchema schema = new RelaxNgSchemaReader { Culture = s_english }.CreateSchema(InputSource.FromString(SchemaUri, Schema));
        string[] documents = ["<doc><a/><b x=\"1\">t</b></doc>", "<doc><b/></doc>", "<doc><c/></doc>", "<doc/>", "<doc><a/><a/></doc>"];
        List<string>[] first = documents.Select(d => Validate(schema, d)).ToArray();

        // Act
        List<string>[] again = documents.Select(d => Validate(schema, d)).ToArray();
        List<string>[] reversed = documents.Reverse().Select(d => Validate(schema, d)).Reverse().ToArray();

        // Assert
        first[0].Should().BeEmpty();
        first[1].Should().NotBeEmpty();
        again.Select(m => string.Join("|", m)).Should().Equal(first.Select(m => string.Join("|", m)));
        reversed.Select(m => string.Join("|", m)).Should().Equal(first.Select(m => string.Join("|", m)));
    }

    [Fact]
    public async Task Validate_OnManyThreadsAtOnce_GivesTheMessagesOfOneThread()
    {
        // Arrange
        RelaxNgSchema schema = new RelaxNgSchemaReader { Culture = s_english }.CreateSchema(InputSource.FromString(SchemaUri, Schema));
        string[] documents = ["<doc><a/><b x=\"1\">t</b></doc>", "<doc><b/></doc>", "<doc><c/></doc>", "<doc/>", "<doc><a/><a/></doc>"];
        string[] expected = documents.Select(d => string.Join("|", Validate(schema, d))).ToArray();

        // Act
        string[][] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 40).SelectMany(_ => documents).Select(d => string.Join("|", Validate(schema, d))).ToArray())));

        // Assert
        foreach (string[] result in results)
        {
            result.Should().Equal(Enumerable.Range(0, 40).SelectMany(_ => expected));
        }
    }

    private static List<string> Validate(RelaxNgSchema schema, string document)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString(null, document));
        return errors.Messages;
    }
}
