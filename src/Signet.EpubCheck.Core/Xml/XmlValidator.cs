using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// A compiled schema of EPUBCheck (upstream's <c>XMLValidator</c>): one of the embedded schemas, read once; validators
/// made from it (one per document) can run in parallel. A <c>.rnc</c> schema is read in RELAX NG's compact syntax, any
/// other by the language its root element says (Jing's <c>AutoSchemaReader</c>). As in EPUBCheck, the message of a
/// failed Schematron assertion is its statement alone (upstream empties Jing's <c>assertion failed:</c> and
/// <c>report:</c>), so that <see cref="ReportingErrorHandler"/> sees its <c>WARNING:</c> / <c>USAGE:</c> prefix.
/// </summary>
public sealed class XmlValidator
{
    private const string CompactExtension = ".rnc";

    /// <summary>Reads the embedded schema at <paramref name="schemaName"/> (<c>schema/30/package-30.rnc</c>, …).</summary>
    /// <exception cref="FileNotFoundException">No schema is embedded at <paramref name="schemaName"/>.</exception>
    /// <exception cref="InvalidOperationException">The schema has errors (upstream's "Internal error").</exception>
    public XmlValidator(string schemaName, bool isNormative = true)
    {
        ArgumentNullException.ThrowIfNull(schemaName);
        SchemaName = schemaName;
        IsNormative = isNormative;

        InputSource source = SchemaResources.Source(schemaName);
        ErrorCollector errors = new();
        try
        {
            Schema = schemaName.EndsWith(CompactExtension, StringComparison.Ordinal)
                ? new RelaxNgSchemaReader { Syntax = RelaxNgSyntax.Compact, ErrorHandler = errors, Resolver = SchemaResources.Resolver }
                    .CreateSchema(source)
                : new AutoSchemaReader { ErrorHandler = errors, Resolver = SchemaResources.Resolver, SchematronStatementsOnly = true }
                    .CreateSchema(source);
        }
        catch (Exception e) when (e is IncorrectSchemaException or SaxException)
        {
            // Upstream prints the errors and throws an Error: an embedded schema with errors is a bug of the build.
            throw new InvalidOperationException(
                "Internal error: " + e.Message + " " + schemaName + Environment.NewLine + string.Join(Environment.NewLine, errors.Messages), e);
        }
    }

    /// <summary>The path of the schema.</summary>
    public string SchemaName { get; }

    /// <summary>The compiled schema.</summary>
    public ISchema Schema { get; }

    /// <summary>Whether the schema is normative: the errors against an informative one are reported as <c>RSC-025</c> /
    /// <c>RSC-024</c> instead of <c>RSC-005</c> / <c>RSC-017</c> (upstream's <c>ReportingErrorHandler</c>).</summary>
    public bool IsNormative { get; }

    /// <summary>
    /// A validator of one document whose errors are reported to <paramref name="report"/> at <paramref name="location"/>
    /// (what upstream's <c>XMLParser.addValidator</c> makes); the messages of the schema languages are in the language of
    /// <paramref name="culture"/> (upstream: the locale of the report). Feed it the events of the document.
    /// </summary>
    public IValidator CreateValidator(IReport report, EpubLocation location, CultureInfo? culture = null) =>
        Schema.CreateValidator(new ReportingErrorHandler(report, location, IsNormative), culture);

    private sealed class ErrorCollector : ISaxErrorHandler
    {
        public List<string> Messages { get; } = [];

        public void OnWarning(SaxParseException exception) => Add(exception);

        public void OnError(SaxParseException exception) => Add(exception);

        public void OnFatalError(SaxParseException exception) => Add(exception);

        private void Add(SaxParseException exception) =>
            Messages.Add(exception.SystemId + ":" + exception.LineNumber + ":" + exception.ColumnNumber + ": " + exception.Message);
    }
}
