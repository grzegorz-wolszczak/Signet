using System;
using System.Globalization;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Parse.Compact;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing;

/// <summary>A schema could not be made: its errors went to the error handler (Jing's <c>IncorrectSchemaException</c>).</summary>
public class IncorrectSchemaException : Exception
{
    /// <summary>Creates the exception.</summary>
    public IncorrectSchemaException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public IncorrectSchemaException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public IncorrectSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The two syntaxes of RELAX NG.</summary>
public enum RelaxNgSyntax
{
    /// <summary>The XML syntax (<c>.rng</c>).</summary>
    Xml,

    /// <summary>The compact syntax (<c>.rnc</c>).</summary>
    Compact,
}

/// <summary>
/// Reads RELAX NG schemas (Jing's RNG <c>SchemaReader</c> and <c>CompactSchemaReader</c> as EPUBCheck configures them:
/// no ID/IDREF checking, no "feasible" mode). EPUBCheck reads a schema whose name ends with <c>.rnc</c> in the compact
/// syntax, any other in the XML syntax; the documents it includes are read in the same syntax.
/// </summary>
public sealed class RelaxNgSchemaReader
{
    /// <summary>The syntax of the schema and of the documents it includes.</summary>
    public RelaxNgSyntax Syntax { get; init; } = RelaxNgSyntax.Xml;

    /// <summary>Where the errors of the schema go (none: they are only counted).</summary>
    public ISaxErrorHandler? ErrorHandler { get; init; }

    /// <summary>Finds the documents of <c>include</c> and <c>externalRef</c>.</summary>
    public ISchemaResolver Resolver { get; init; } = BasicSchemaResolver.Instance;

    /// <summary>The datatype libraries besides the built-in one (none yet: the XML Schema datatypes come with task 2.3).</summary>
    public IDatatypeLibraryFactory? DatatypeLibraryFactory { get; init; }

    /// <summary>The language of the messages about the schema (<see cref="CultureInfo.CurrentUICulture"/> when <c>null</c>).</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Reads, simplifies and checks the schema of <paramref name="source"/>.</summary>
    /// <exception cref="IncorrectSchemaException">The schema has errors (reported to <see cref="ErrorHandler"/>).</exception>
    /// <exception cref="SaxParseException">A schema document in the XML syntax is not well-formed.</exception>
    public RelaxNgSchema CreateSchema(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SchemaPatternBuilder spb = new();
        JingLocalizer patternLocalizer = JingLocalizer.ForPattern(Culture);
        ISubParseable parseable = Syntax == RelaxNgSyntax.Compact
            ? new CompactParseable(source, Resolver, ErrorHandler, JingLocalizer.ForCompact(Culture))
            : new SaxParseable(source, Resolver, ErrorHandler, JingLocalizer.ForParse(Culture));
        try
        {
            Pattern start = SchemaBuilderImpl.Parse(parseable, ErrorHandler, DatatypeLibraryFactory, spb, isAttributesPattern: false, patternLocalizer);
            return new RelaxNgSchema(spb, start);
        }
        catch (IllegalSchemaException e)
        {
            throw new IncorrectSchemaException("The schema has errors.", e);
        }
    }
}

/// <summary>A simplified, checked RELAX NG schema (Jing's <c>PatternSchema</c>); validators made from it can run in parallel.</summary>
public sealed class RelaxNgSchema
{
    private readonly SchemaPatternBuilder _spb;
    private readonly Pattern _start;

    internal RelaxNgSchema(SchemaPatternBuilder spb, Pattern start)
    {
        _spb = spb;
        _start = start;
    }

    /// <summary>The simplified pattern of the schema (for the tests that compare two schemas).</summary>
    internal Pattern Start => _start;

    /// <summary>A validator whose errors go to <paramref name="errorHandler"/>, worded in the language of <paramref name="culture"/>.</summary>
    public RelaxNgValidator CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        return new RelaxNgValidator(new PatternValidator(_start, new ValidatorPatternBuilder(_spb), errorHandler,
            JingLocalizer.ForPattern(culture)), errorHandler);
    }
}

/// <summary>Validates one document at a time against a schema (Jing's <c>Validator</c>): feed it the events of the document.</summary>
public sealed class RelaxNgValidator
{
    private readonly PatternValidator _validator;
    private readonly ISaxErrorHandler _errorHandler;

    internal RelaxNgValidator(PatternValidator validator, ISaxErrorHandler errorHandler)
    {
        _validator = validator;
        _errorHandler = errorHandler;
    }

    /// <summary>The handler that receives the content of the document.</summary>
    public IContentHandler ContentHandler => _validator;

    /// <summary>The handler that receives the DTD declarations of the document.</summary>
    public IDtdHandler DtdHandler => _validator;

    /// <summary>Makes the validator ready for another document.</summary>
    public void Reset() => _validator.Reset();

    /// <summary>Reads <paramref name="document"/> and validates it: the errors go to the error handler.</summary>
    /// <exception cref="SaxParseException">The document is not well-formed (reported as a fatal error too).</exception>
    public void Validate(InputSource document)
    {
        XmlEventReader reader = new() { ContentHandler = ContentHandler, DtdHandler = DtdHandler, ErrorHandler = _errorHandler };
        reader.Parse(document);
    }
}
