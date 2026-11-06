using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using System.Text;

namespace Signet.EpubCheck.Core.Jing;

/// <summary>
/// The texts of Jing's messages in one language (Jing's <c>Localizer</c>, as EPUBCheck patches it to follow the
/// locale of the check). English is the neutral resource, Polish has its own; any other language gets English. The
/// texts keep Jing's <c>java.text.MessageFormat</c> patterns (<c>{0}</c>, quotes doubled), filled by
/// <see cref="Format"/>.
/// </summary>
internal sealed class JingLocalizer
{
    private static readonly ResourceManager s_patternMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.PatternMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_parseMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.ParseMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_compactMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.CompactMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_xsdDatatypeMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.XsdDatatypeMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_regexMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.RegexMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_nvdlMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.NvdlMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_autoSchemaMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.AutoSchemaMessages", typeof(JingLocalizer).Assembly);

    private static readonly ResourceManager s_schematronMessages =
        new("Signet.EpubCheck.Core.Jing.Resources.SchematronMessages", typeof(JingLocalizer).Assembly);

    private static readonly ConcurrentDictionary<(ResourceManager, string), JingLocalizer> s_instances = new();

    private const string GroupedIntegerFormat = "#,##0";

    private readonly ResourceManager _manager;
    private readonly CultureInfo _culture;

    private JingLocalizer(ResourceManager manager, CultureInfo culture)
    {
        _manager = manager;
        _culture = culture;
    }

    /// <summary>The raw resources of the pattern module (schema building and validation), for the tests.</summary>
    internal static ResourceManager PatternManager => s_patternMessages;

    /// <summary>The raw resources of the XML syntax parser, for the tests.</summary>
    internal static ResourceManager ParseManager => s_parseMessages;

    /// <summary>The raw resources of the compact syntax parser, for the tests.</summary>
    internal static ResourceManager CompactManager => s_compactMessages;

    /// <summary>The raw resources of the XML Schema datatypes, for the tests.</summary>
    internal static ResourceManager XsdDatatypeManager => s_xsdDatatypeMessages;

    /// <summary>The raw resources of the regular expression translator, for the tests.</summary>
    internal static ResourceManager RegexManager => s_regexMessages;

    /// <summary>The raw resources of NVDL, for the tests.</summary>
    internal static ResourceManager NvdlManager => s_nvdlMessages;

    /// <summary>The raw resources of the schema language detection, for the tests.</summary>
    internal static ResourceManager AutoSchemaManager => s_autoSchemaMessages;

    /// <summary>The raw resources of Schematron, for the tests.</summary>
    internal static ResourceManager SchematronManager => s_schematronMessages;

    /// <summary>The messages of schema building and validation (Jing's <c>SchemaBuilderImpl.localizer</c>).</summary>
    public static JingLocalizer ForPattern(CultureInfo? culture) => Get(s_patternMessages, culture);

    /// <summary>The messages of the XML syntax parser (Jing's <c>SchemaParser.localizer</c>).</summary>
    public static JingLocalizer ForParse(CultureInfo? culture) => Get(s_parseMessages, culture);

    /// <summary>The messages of the compact syntax parser (Jing's <c>CompactSyntax.localizer</c>).</summary>
    public static JingLocalizer ForCompact(CultureInfo? culture) => Get(s_compactMessages, culture);

    /// <summary>The messages of the XML Schema datatypes (Jing's <c>DatatypeBuilderImpl.localizer</c>).</summary>
    public static JingLocalizer ForXsdDatatypes(CultureInfo? culture) => Get(s_xsdDatatypeMessages, culture);

    /// <summary>The messages of the regular expression translator (Jing's <c>Translator.localizer</c>).</summary>
    public static JingLocalizer ForRegex(CultureInfo? culture) => Get(s_regexMessages, culture);

    /// <summary>The messages of NVDL schemas and validation (Jing's NVDL <c>SchemaImpl</c> and <c>ValidatorImpl</c> localizers).</summary>
    public static JingLocalizer ForNvdl(CultureInfo? culture) => Get(s_nvdlMessages, culture);

    /// <summary>The messages of the schema language detection (Jing's <c>AutoSchemaReceiver</c> localizer).</summary>
    public static JingLocalizer ForAutoSchema(CultureInfo? culture) => Get(s_autoSchemaMessages, culture);

    /// <summary>The messages of Schematron schemas and validation (Jing's Schematron localizers).</summary>
    public static JingLocalizer ForSchematron(CultureInfo? culture) => Get(s_schematronMessages, culture);

    /// <summary>
    /// An integer as <c>MessageFormat</c> writes a number argument: with the grouping separator of the language
    /// (<c>1,000</c> in English).
    /// </summary>
    public string Number(int value) => value.ToString(GroupedIntegerFormat, _culture);

    /// <summary>The text of <paramref name="key"/> with its arguments.</summary>
    /// <exception cref="MissingManifestResourceException">There is no such key.</exception>
    public string Message(string key, params string?[] args)
    {
        string pattern = _manager.GetString(key, _culture)
            ?? throw new MissingManifestResourceException("No Jing message " + key);
        return Format(pattern, args);
    }

    /// <summary>
    /// <c>java.text.MessageFormat.format</c> for the patterns Jing uses: <c>{n}</c> is argument <c>n</c> (<c>null</c>
    /// when it is null, as Java prints it), text between single quotes is literal and <c>''</c> is a quote.
    /// </summary>
    internal static string Format(string pattern, string?[] args)
    {
        const char Quote = '\'';
        const char OpenBrace = '{';
        const char CloseBrace = '}';
        const string NullText = "null";
        StringBuilder result = new();
        bool quoted = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == Quote)
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == Quote)
                {
                    result.Append(Quote);
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == OpenBrace && !quoted)
            {
                int end = pattern.IndexOf(CloseBrace, i + 1);
                if (end < 0)
                {
                    throw new FormatException("Unmatched braces in the pattern: " + pattern);
                }

                int index = int.Parse(pattern.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture);
                result.Append(index < args.Length ? args[index] ?? NullText : OpenBrace + index.ToString(CultureInfo.InvariantCulture) + CloseBrace);
                i = end;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static JingLocalizer Get(ResourceManager manager, CultureInfo? culture)
    {
        string language = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        if (!LocalizedLanguages.IsSupported(language))
        {
            language = LocalizedLanguages.Default;
        }

        return s_instances.GetOrAdd((manager, language), static key => new JingLocalizer(key.Item1, CultureInfo.GetCultureInfo(key.Item2)));
    }

    private static class LocalizedLanguages
    {
        public const string Default = "en";
        private const string Polish = "pl";

        public static bool IsSupported(string language) =>
            string.Equals(language, Default, StringComparison.Ordinal) || string.Equals(language, Polish, StringComparison.Ordinal);
    }
}
