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

    private static readonly ConcurrentDictionary<(ResourceManager, string), JingLocalizer> s_instances = new();

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

    /// <summary>The messages of schema building and validation (Jing's <c>SchemaBuilderImpl.localizer</c>).</summary>
    public static JingLocalizer ForPattern(CultureInfo? culture) => Get(s_patternMessages, culture);

    /// <summary>The messages of the XML syntax parser (Jing's <c>SchemaParser.localizer</c>).</summary>
    public static JingLocalizer ForParse(CultureInfo? culture) => Get(s_parseMessages, culture);

    /// <summary>The messages of the compact syntax parser (Jing's <c>CompactSyntax.localizer</c>).</summary>
    public static JingLocalizer ForCompact(CultureInfo? culture) => Get(s_compactMessages, culture);

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
