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
/// The XML file of a theme of the interface (<c>*.signet-theme.xml</c>) or of a color scheme (<c>*.signet-scheme.xml</c>,
/// root <c>signet-scheme</c>), modelled on IntelliJ's <c>.icls</c>:
/// <code>
/// &lt;signet-theme version="1" id="…" name="Sepia" parent="signet-light" variant="Light"&gt;
///   &lt;meta created="2026-10-12T14:32:00Z" modified="…" signet="0.8.0-…" /&gt;
///   &lt;colors&gt;&lt;color key="editor.currentLine" value="#F5ECD7" /&gt;&lt;/colors&gt;
///   &lt;attributes&gt;
///     &lt;attribute key="xhtml.tag" foreground="#8B4513" bold="true" /&gt;
///     &lt;attribute key="errors.spelling" effect="wave" effect-color="#C0392B" /&gt;
///     &lt;attribute key="xhtml.css.comment" inherit="true" /&gt;
///   &lt;/attributes&gt;
/// &lt;/signet-theme&gt;
/// </code>
/// A file holds what the theme sets itself (for a theme of the user: what differs from its parent). Keys this version
/// does not know, written in the section of the other kind or belonging to the other document (an editor color in a
/// theme of the interface, an interface color in a color scheme), are skipped with a warning; a broken value or a newer
/// format stops the reading with a <see cref="ThemeFormatException"/> naming the line.
/// </summary>
public static class ThemeXml
{
    /// <summary>The version of the format written (and the newest one read).</summary>
    public const int Version = 1;

    /// <summary>The extension of a theme file.</summary>
    public const string Extension = ".signet-theme.xml";

    /// <summary>The extension of a color scheme file.</summary>
    public const string SchemeExtension = ".signet-scheme.xml";

    /// <summary>The extension of the files of a kind of document.</summary>
    public static string ExtensionOf(ThemeDocumentKind kind) => kind == ThemeDocumentKind.Theme ? Extension : SchemeExtension;

    private static string RootNameOf(ThemeDocumentKind kind) => kind == ThemeDocumentKind.Theme ? "signet-theme" : "signet-scheme";

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

    /// <summary>Reads a theme or a color scheme.</summary>
    /// <param name="reader">The XML text.</param>
    /// <param name="kind">The kind of document expected: a file of the other kind is not read.</param>
    /// <param name="warnings">Receives what was skipped (unknown keys, keys in the wrong section); <c>null</c> = ignore.</param>
    /// <exception cref="ThemeFormatException">The text is not a document of this kind this version can read.</exception>
    public static Theme Read(TextReader reader, ThemeDocumentKind kind, ICollection<string>? warnings = null)
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
        if (root.Name.LocalName != RootNameOf(kind))
        {
            throw new ThemeFormatException(
                CoreStrings.Get(kind == ThemeDocumentKind.Theme ? "Theme_NotATheme" : "Theme_NotAScheme"), Line(root));
        }

        int version = Int(root, "version", required: true)!.Value;
        if (version > Version)
        {
            throw new ThemeFormatException(CoreStrings.Format("Theme_UnsupportedVersion", version), Line(root));
        }

        Theme theme = new(Required(root, "id"), Required(root, "name"))
        {
            ParentId = Optional(root, "parent"),
            Variant = kind == ThemeDocumentKind.Theme && Optional(root, "variant") is { } variant
                ? Enum<InterfaceVariant>(root, "variant", variant)
                : null,
        };

        if (root.Element("meta") is { } meta)
        {
            theme = theme with { Created = Date(meta, "created"), Modified = Date(meta, "modified") };
        }

        ImmutableDictionary<string, ThemeEntry>.Builder entries =
            ImmutableDictionary.CreateBuilder<string, ThemeEntry>(StringComparer.Ordinal);
        foreach (XElement color in Children(root, "colors", "color"))
        {
            if (Known(color, ThemeKeyKind.Color, kind, warnings) is { } key)
            {
                entries[key.Id] = Bool(color, "inherit") == true
                    ? ThemeEntry.Inherited
                    : ThemeEntry.Of(Color(color, "value"));
            }
        }

        foreach (XElement attribute in Children(root, "attributes", "attribute"))
        {
            if (Known(attribute, ThemeKeyKind.TextAttributes, kind, warnings) is { } key)
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

        return theme with { Entries = entries.ToImmutable() };
    }

    /// <summary>Writes a theme or a color scheme: the keys in the order of the registry, only what it sets itself.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="kind">The kind of document written (the name of the root).</param>
    /// <param name="writer">Receives the XML text.</param>
    /// <param name="writtenBy">The Signet version written to <c>meta/@signet</c>; <c>null</c> = none.</param>
    public static void Write(Theme theme, ThemeDocumentKind kind, TextWriter writer, string? writtenBy = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(writer);
        XElement root = new(RootNameOf(kind),
            new XAttribute("version", Version),
            new XAttribute("id", theme.Id),
            new XAttribute("name", theme.Name),
            theme.ParentId is { } parent ? new XAttribute("parent", parent) : null,
            theme.Variant is { } variant ? new XAttribute("variant", variant.ToString()) : null);

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

    /// <summary>The theme or color scheme as XML text (<see cref="Write"/>).</summary>
    public static string ToXml(Theme theme, ThemeDocumentKind kind = ThemeDocumentKind.Theme, string? writtenBy = null)
    {
        using Utf8StringWriter writer = new();
        Write(theme, kind, writer, writtenBy);
        return writer.ToString();
    }

    /// <summary>Reads a theme or color scheme from XML text (<see cref="Read"/>).</summary>
    public static Theme FromXml(string xml, ThemeDocumentKind kind = ThemeDocumentKind.Theme, ICollection<string>? warnings = null)
    {
        using StringReader reader = new(xml ?? throw new ArgumentNullException(nameof(xml)));
        return Read(reader, kind, warnings);
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

    // The key of an entry, when this version knows it, it is in the section of its kind and belongs to the document read.
    private static ThemeKey? Known(XElement element, ThemeKeyKind kind, ThemeDocumentKind document, ICollection<string>? warnings)
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

        if (key.Document != document)
        {
            warnings?.Add(CoreStrings.Format("Theme_WrongDocument", Line(element), id));
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
