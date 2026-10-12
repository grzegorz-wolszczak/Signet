using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Themes;

/// <summary>
/// The XML file of a theme (<c>*.signet-theme.xml</c>), modelled on IntelliJ's <c>.icls</c>:
/// <code>
/// &lt;signet-theme version="1" id="…" name="Sepia" parent="signet-light" variant="Light" icon-set="material"&gt;
///   &lt;meta created="2026-10-12T14:32:00Z" modified="…" signet="0.8.0-…" /&gt;
///   &lt;colors&gt;&lt;color key="editor.currentLine" value="#F5ECD7" /&gt;&lt;/colors&gt;
///   &lt;attributes&gt;
///     &lt;attribute key="xhtml.tag" foreground="#8B4513" bold="true" /&gt;
///     &lt;attribute key="errors.spelling" effect="wave" effect-color="#C0392B" /&gt;
///     &lt;attribute key="xhtml.css.comment" inherit="true" /&gt;
///   &lt;/attributes&gt;
///   &lt;fonts use-theme-fonts="true"&gt;&lt;font key="codeView.editor" family="Cascadia Code" size="13" /&gt;&lt;/fonts&gt;
/// &lt;/signet-theme&gt;
/// </code>
/// A file holds what the theme sets itself (for a theme of the user: what differs from its parent). Keys this version
/// does not know, or written in the section of the other kind, are skipped with a warning; a broken value or a newer
/// format stops the reading with a <see cref="ThemeFormatException"/> naming the line.
/// </summary>
public static class ThemeXml
{
    /// <summary>The version of the format written (and the newest one read).</summary>
    public const int Version = 1;

    /// <summary>The extension of a theme file.</summary>
    public const string Extension = ".signet-theme.xml";

    private const string RootName = "signet-theme";

    private static readonly Dictionary<EffectType, string> EffectNames = new()
    {
        [EffectType.None] = "none",
        [EffectType.Underline] = "underline",
        [EffectType.BoldUnderline] = "boldUnderline",
        [EffectType.Wave] = "wave",
        [EffectType.DottedUnderline] = "dottedUnderline",
        [EffectType.DashedUnderline] = "dashedUnderline",
        [EffectType.Box] = "box",
        [EffectType.Strikeout] = "strikeout",
    };

    /// <summary>Reads a theme.</summary>
    /// <param name="reader">The XML text.</param>
    /// <param name="warnings">Receives what was skipped (unknown keys, keys in the wrong section); <c>null</c> = ignore.</param>
    /// <exception cref="ThemeFormatException">The text is not a theme this version can read.</exception>
    public static Theme Read(TextReader reader, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        XDocument document;
        try
        {
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            throw new ThemeFormatException(CoreStrings.Format("Theme_InvalidXml", e.Message), e.LineNumber);
        }

        XElement root = document.Root!;
        if (root.Name.LocalName != RootName)
        {
            throw new ThemeFormatException(CoreStrings.Get("Theme_NotATheme"), Line(root));
        }

        int version = Int(root, "version", required: true)!.Value;
        if (version > Version)
        {
            throw new ThemeFormatException(CoreStrings.Format("Theme_UnsupportedVersion", version), Line(root));
        }

        Theme theme = new(Required(root, "id"), Required(root, "name"))
        {
            ParentId = Optional(root, "parent"),
            Variant = Optional(root, "variant") is { } variant ? Enum<InterfaceVariant>(root, "variant", variant) : null,
            IconSet = Optional(root, "icon-set"),
        };

        if (root.Element("meta") is { } meta)
        {
            theme = theme with { Created = Date(meta, "created"), Modified = Date(meta, "modified") };
        }

        ImmutableDictionary<string, ThemeEntry>.Builder entries =
            ImmutableDictionary.CreateBuilder<string, ThemeEntry>(StringComparer.Ordinal);
        foreach (XElement color in Children(root, "colors", "color"))
        {
            if (Known(color, ThemeKeyKind.Color, warnings) is { } key)
            {
                entries[key.Id] = Bool(color, "inherit") == true
                    ? ThemeEntry.Inherited
                    : ThemeEntry.Of(Color(color, "value"));
            }
        }

        foreach (XElement attribute in Children(root, "attributes", "attribute"))
        {
            if (Known(attribute, ThemeKeyKind.TextAttributes, warnings) is { } key)
            {
                entries[key.Id] = Bool(attribute, "inherit") == true
                    ? ThemeEntry.Inherited
                    : ThemeEntry.Of(new TextAttributes
                    {
                        Foreground = Color(attribute, "foreground"),
                        Background = Color(attribute, "background"),
                        Bold = Bool(attribute, "bold") ?? false,
                        Italic = Bool(attribute, "italic") ?? false,
                        Effect = Optional(attribute, "effect") is { } effect ? Effect(attribute, effect) : EffectType.None,
                        EffectColor = Color(attribute, "effect-color"),
                    });
            }
        }

        ImmutableDictionary<string, ThemeFont>.Builder fonts = ImmutableDictionary.CreateBuilder<string, ThemeFont>(StringComparer.Ordinal);
        foreach (XElement font in Children(root, "fonts", "font"))
        {
            fonts[Required(font, "key")] = new ThemeFont(Optional(font, "family") ?? string.Empty, Int(font, "size", required: false) ?? 0);
        }

        return theme with
        {
            Entries = entries.ToImmutable(),
            Fonts = fonts.ToImmutable(),
            UseThemeFonts = root.Element("fonts") is { } fontsElement && Bool(fontsElement, "use-theme-fonts") == true,
        };
    }

    /// <summary>Writes a theme: the keys in the order of the registry, only what the theme sets itself.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="writer">Receives the XML text.</param>
    /// <param name="writtenBy">The Signet version written to <c>meta/@signet</c>; <c>null</c> = none.</param>
    public static void Write(Theme theme, TextWriter writer, string? writtenBy = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(writer);
        XElement root = new(RootName,
            new XAttribute("version", Version),
            new XAttribute("id", theme.Id),
            new XAttribute("name", theme.Name),
            theme.ParentId is { } parent ? new XAttribute("parent", parent) : null,
            theme.Variant is { } variant ? new XAttribute("variant", variant.ToString()) : null,
            theme.IconSet is { } iconSet ? new XAttribute("icon-set", iconSet) : null);

        if (theme.Created is not null || theme.Modified is not null || writtenBy is not null)
        {
            root.Add(new XElement("meta",
                theme.Created is { } created ? new XAttribute("created", Format(created)) : null,
                theme.Modified is { } modified ? new XAttribute("modified", Format(modified)) : null,
                writtenBy is not null ? new XAttribute("signet", writtenBy) : null));
        }

        List<KeyValuePair<string, ThemeEntry>> ordered = Ordered(theme);
        List<XElement> colors = ordered
            .Where(e => !ThemeKeys.TryGet(e.Key, out ThemeKey? k) || k.Kind == ThemeKeyKind.Color)
            .Where(e => e.Value.Attributes is null)
            .Select(e => new XElement("color", new XAttribute("key", e.Key),
                e.Value.Inherit ? new XAttribute("inherit", "true") : null,
                !e.Value.Inherit && e.Value.Color is { } c ? new XAttribute("value", c.ToString()) : null))
            .ToList();
        List<XElement> attributes = ordered
            .Where(e => e.Value.Attributes is not null || (e.Value.Inherit && ThemeKeys.TryGet(e.Key, out ThemeKey? k)
                                                           && k.Kind == ThemeKeyKind.TextAttributes))
            .Select(e => AttributeElement(e.Key, e.Value))
            .ToList();
        if (colors.Count > 0)
        {
            root.Add(new XElement("colors", colors));
        }

        if (attributes.Count > 0)
        {
            root.Add(new XElement("attributes", attributes));
        }

        if (theme.UseThemeFonts || theme.Fonts.Count > 0)
        {
            root.Add(new XElement("fonts",
                new XAttribute("use-theme-fonts", theme.UseThemeFonts ? "true" : "false"),
                theme.Fonts.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new XElement("font",
                    new XAttribute("key", f.Key),
                    f.Value.Family.Length > 0 ? new XAttribute("family", f.Value.Family) : null,
                    f.Value.Size > 0 ? new XAttribute("size", f.Value.Size) : null))));
        }

        using XmlWriter xml = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = new System.Text.UTF8Encoding(false),
            CloseOutput = false,
        });
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(xml);
    }

    /// <summary>The theme as XML text (<see cref="Write"/>).</summary>
    public static string ToXml(Theme theme, string? writtenBy = null)
    {
        using Utf8StringWriter writer = new();
        Write(theme, writer, writtenBy);
        return writer.ToString();
    }

    /// <summary>Reads a theme from XML text (<see cref="Read"/>).</summary>
    public static Theme FromXml(string xml, ICollection<string>? warnings = null)
    {
        using StringReader reader = new(xml ?? throw new ArgumentNullException(nameof(xml)));
        return Read(reader, warnings);
    }

    private static XElement AttributeElement(string key, ThemeEntry entry)
    {
        XElement element = new("attribute", new XAttribute("key", key));
        if (entry.Inherit)
        {
            element.Add(new XAttribute("inherit", "true"));
            return element;
        }

        TextAttributes a = entry.Attributes!;
        element.Add(
            a.Foreground is { } fg ? new XAttribute("foreground", fg.ToString()) : null,
            a.Background is { } bg ? new XAttribute("background", bg.ToString()) : null,
            a.Bold ? new XAttribute("bold", "true") : null,
            a.Italic ? new XAttribute("italic", "true") : null,
            a.Effect != EffectType.None ? new XAttribute("effect", EffectNames[a.Effect]) : null,
            a.EffectColor is { } ec ? new XAttribute("effect-color", ec.ToString()) : null);
        return element;
    }

    // The registry's order first, then the keys it does not know (in ordinal order) — a stable file for diffs.
    private static List<KeyValuePair<string, ThemeEntry>> Ordered(Theme theme)
    {
        Dictionary<string, int> rank = ThemeKeys.All.Select((k, i) => (k.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        return theme.Entries
            .OrderBy(e => rank.TryGetValue(e.Key, out int i) ? i : int.MaxValue)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<XElement> Children(XElement root, string section, string item) =>
        root.Elements(section).Elements(item);

    // The key of an entry, when this version knows it and it is in the section of its kind.
    private static ThemeKey? Known(XElement element, ThemeKeyKind kind, ICollection<string>? warnings)
    {
        string id = Required(element, "key");
        if (!ThemeKeys.TryGet(id, out ThemeKey? key))
        {
            warnings?.Add(CoreStrings.Format("Theme_UnknownKey", Line(element), id));
            return null;
        }

        if (key.Kind != kind)
        {
            warnings?.Add(CoreStrings.Format("Theme_WrongSection", Line(element), id));
            return null;
        }

        return key;
    }

    private static string Required(XElement element, string name) =>
        Optional(element, name) is { Length: > 0 } value
            ? value
            : throw new ThemeFormatException(CoreStrings.Format("Theme_MissingAttribute", Line(element), name), Line(element));

    private static string? Optional(XElement element, string name) => element.Attribute(name)?.Value;

    private static ThemeColor? Color(XElement element, string name)
    {
        if (Optional(element, name) is not { } text)
        {
            return null;
        }

        return ThemeColor.TryParse(text, out ThemeColor color) ? color : throw Invalid(element, name, text);
    }

    private static bool? Bool(XElement element, string name) => Optional(element, name) switch
    {
        null => null,
        "true" => true,
        "false" => false,
        { } text => throw Invalid(element, name, text),
    };

    private static int? Int(XElement element, string name, bool required)
    {
        string? text = required ? Required(element, name) : Optional(element, name);
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw Invalid(element, name, text);
    }

    private static DateTimeOffset? Date(XElement element, string name)
    {
        if (Optional(element, name) is not { } text)
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value)
            ? value
            : throw Invalid(element, name, text);
    }

    private static T Enum<T>(XElement element, string name, string text)
        where T : struct, Enum =>
        text.All(char.IsAsciiLetter) && System.Enum.TryParse(text, ignoreCase: false, out T value) && System.Enum.IsDefined(value)
            ? value
            : throw Invalid(element, name, text);

    private static EffectType Effect(XElement element, string text) =>
        EffectNames.FirstOrDefault(p => p.Value == text) is { Value: not null } pair ? pair.Key : throw Invalid(element, "effect", text);

    private static ThemeFormatException Invalid(XElement element, string name, string value) =>
        new(CoreStrings.Format("Theme_InvalidValue", Line(element), name, value), Line(element));

    private static string Format(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static int Line(XObject node) => node is IXmlLineInfo { } info && info.HasLineInfo() ? info.LineNumber : 0;

    // The XML declaration names the encoding of the writer: UTF-8, as the files are written.
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }
}

/// <summary>A theme file this version cannot read (<see cref="ThemeXml.Read"/>); the message names the line.</summary>
public sealed class ThemeFormatException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public ThemeFormatException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public ThemeFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public ThemeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with a message and the line of the problem.</summary>
    public ThemeFormatException(string message, int line)
        : base(message) => Line = line;

    /// <summary>The line of the problem (1-based); 0 = unknown.</summary>
    public int Line { get; }
}
