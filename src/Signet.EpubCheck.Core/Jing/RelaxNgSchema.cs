using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Parse.Compact;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;

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

    /// <summary>Whether the schema validates the attributes of an NVDL attribute section (Jing's <c>WrapProperty.ATTRIBUTE_OWNER</c>).</summary>
    internal bool IsAttributesSchema { get; init; }

    /// <summary>Where the errors of the schema go (none: they are only counted).</summary>
    public ISaxErrorHandler? ErrorHandler { get; init; }

    /// <summary>Finds the documents of <c>include</c> and <c>externalRef</c>.</summary>
    public ISchemaResolver Resolver { get; init; } = BasicSchemaResolver.Instance;

    /// <summary>
    /// The datatype libraries besides the built-in one; when <c>null</c>, the XML Schema datatypes
    /// (<see cref="XsdDatatypeLibraryFactory"/>, with messages in the language of <see cref="Culture"/>), as Jing's
    /// datatype library loader finds them.
    /// </summary>
    public IDatatypeLibraryFactory? DatatypeLibraryFactory { get; init; }

    /// <summary>The language of the messages about the schema (<see cref="CultureInfo.CurrentUICulture"/> when <c>null</c>).</summary>
    public CultureInfo? Culture { get; init; }

    internal static RelaxNgSchemaReader From(SchemaReadSettings settings, RelaxNgSyntax syntax, bool isAttributesSchema) => new()
    {
        ErrorHandler = settings.ErrorHandler,
        Resolver = settings.Resolver,
        DatatypeLibraryFactory = settings.DatatypeLibraryFactory,
        Culture = settings.Culture,
        Syntax = syntax,
        IsAttributesSchema = isAttributesSchema,
    };

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
            IDatatypeLibraryFactory datatypes = DatatypeLibraryFactory ?? XsdDatatypeLibraryFactory.For(Culture);
            Pattern start = SchemaBuilderImpl.Parse(parseable, ErrorHandler, datatypes, spb, IsAttributesSchema, patternLocalizer);
            return new RelaxNgSchema(spb, start);
        }
        catch (IllegalSchemaException e)
        {
            throw new IncorrectSchemaException("The schema has errors.", e);
        }
    }
}

/// <summary>A simplified, checked RELAX NG schema (Jing's <c>PatternSchema</c>); validators made from it can run in parallel.</summary>
public sealed class RelaxNgSchema : ISchema
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
    IValidator ISchema.CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture) => CreateValidator(errorHandler, culture);

    /// <summary>A validator whose errors go to <paramref name="errorHandler"/>, worded in the language of <paramref name="culture"/>.</summary>
    public RelaxNgValidator CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        return new RelaxNgValidator(new PatternValidator(_start, this, errorHandler, JingLocalizer.ForPattern(culture)), errorHandler);
    }

    // The derivatives a validator computes are a function of the schema and the names it meets, never of the document, so the builder that
    // memoizes them is kept between documents (upstream builds a new one per validator and throws the memo away after every document: on
    // a book of a thousand files that was a quarter of everything allocated). A builder is not thread-safe, so each thread has its own;
    // validators that run one inside another on a thread (the sections of an NVDL schema) may share it, as a memo only ever adds
    // results that stay true. The builder of a thread is taken when a validator is made and when it is reset for a new document.
    private const int MaxPooledMemos = 200_000;

    // Per thread, and weak in the schema, so a schema nobody holds any more takes its builders with it.
    [ThreadStatic]
    private static ConditionalWeakTable<RelaxNgSchema, ValidatorPatternBuilder>? t_builders;

    internal ValidatorPatternBuilder BuilderForThread()
    {
        ConditionalWeakTable<RelaxNgSchema, ValidatorPatternBuilder> builders = t_builders ??= [];

        // A builder that has memoized very many patterns is let go, so a long session cannot grow it without bound.
        if (!builders.TryGetValue(this, out ValidatorPatternBuilder? builder) || builder.MemoCount > MaxPooledMemos)
        {
            builder = new ValidatorPatternBuilder(_spb);
            builders.AddOrUpdate(this, builder);
        }

        return builder;
    }
}

/// <summary>Validates one document at a time against a schema (Jing's <c>Validator</c>): feed it the events of the document.</summary>
public sealed class RelaxNgValidator : IValidator
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
