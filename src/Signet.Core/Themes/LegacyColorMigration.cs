using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;
using Signet.Core.Misc;

namespace Signet.Core.Themes;

/// <summary>
/// Moves the colors the settings held before the themes (<see cref="SettingsStore.LegacyColors"/>) into a theme of the
/// user, "My colors", based on Signet Light, and makes it the theme in use. Only the colors that differ from their
/// defaults move; when none does, no theme is made. The old keys are dropped from the settings either way.
/// </summary>
public static class LegacyColorMigration
{
    // Which part of a theme key an old setting becomes.
    private enum Part
    {
        Color,
        Foreground,
        Background,
        EffectColor,
    }

    // The old key, its default (the value of a never-changed setting) and where it goes.
    private static readonly (string Key, string Default, ThemeKey Target, Part Part)[] Mappings =
    {
        ("code_editor_css_comment_color", "#008000", ThemeKeys.CssComment, Part.Foreground),
        ("code_editor_css_property_color", "#000080", ThemeKeys.CssProperty, Part.Foreground),
        ("code_editor_css_quote_color", "#800080", ThemeKeys.CssString, Part.Foreground),
        ("code_editor_css_selector_color", "#800000", ThemeKeys.CssSelector, Part.Foreground),
        ("code_editor_css_value_color", "#000000", ThemeKeys.CssValue, Part.Foreground),
        ("code_editor_css_special_selector_color", "#AA5500", ThemeKeys.CssSpecialSelector, Part.Foreground),
        ("code_editor_css_at_rule_color", "#8B008B", ThemeKeys.CssAtRule, Part.Foreground),
        ("code_editor_css_constant_color", "#098658", ThemeKeys.CssConstant, Part.Foreground),
        ("code_editor_line_highlight_color", "#FFFFBF", ThemeKeys.EditorCurrentLine, Part.Color),
        ("code_editor_xhtml_attribute_name_color", "#800000", ThemeKeys.XhtmlAttributeName, Part.Foreground),
        ("code_editor_xhtml_attribute_value_color", "#008080", ThemeKeys.XhtmlAttributeValue, Part.Foreground),
        ("code_editor_xhtml_css_color", "#808000", ThemeKeys.XhtmlCss, Part.Foreground),
        ("code_editor_xhtml_css_comment_color", "#008000", ThemeKeys.XhtmlCssComment, Part.Foreground),
        ("code_editor_xhtml_doctype_color", "#000080", ThemeKeys.XhtmlDoctype, Part.Foreground),
        ("code_editor_xhtml_entity_color", "#800080", ThemeKeys.XhtmlEntity, Part.Foreground),
        // The dashed line under a special space was drawn in the entity color.
        ("code_editor_xhtml_entity_color", "#800080", ThemeKeys.XhtmlSpecialSpace, Part.EffectColor),
        ("code_editor_xhtml_html_color", "#0000FF", ThemeKeys.XhtmlTag, Part.Foreground),
        ("code_editor_xhtml_html_comment_color", "#008000", ThemeKeys.XhtmlComment, Part.Foreground),
        ("code_editor_xhtml_namespace_prefix_color", "#7B5694", ThemeKeys.XhtmlNamespacePrefix, Part.Foreground),
        ("code_editor_link_color", "#0645AD", ThemeKeys.Link, Part.Foreground),
        ("code_editor_error_underline_color", "#FF0000", ThemeKeys.ErrorsSyntax, Part.EffectColor),
        ("code_editor_search_match_background_color", "#FFF0C4", ThemeKeys.SearchMatch, Part.Background),
        ("code_editor_selection_background_color", "#ADD6FF", ThemeKeys.EditorSelection, Part.Background),
        ("warning_color_light", "#B35C00", ThemeKeys.UiWarningText, Part.Color),
        ("error_color_light", "#D93030", ThemeKeys.UiErrorNotification, Part.Color),
        ("open_tag_hint_light_background", "#F2F2F2", ThemeKeys.HintsOpenTag, Part.Background),
        ("open_tag_hint_light_foreground", "#000000", ThemeKeys.HintsOpenTag, Part.Foreground),
        ("preview_highlight_color_light", "#FFEB3B", ThemeKeys.PreviewHighlight, Part.Color),
    };

    /// <summary>The old keys of the colors (dropped from the settings when they are loaded).</summary>
    public static IReadOnlyList<string> Keys { get; } = Mappings.Select(m => m.Key).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The default of every old color: a setting never changed migrates to nothing, so these must equal the defaults of
    /// the keys they go to (checked by the tests).
    /// </summary>
    public static IEnumerable<(string Key, ThemeColor Default, ThemeKey Target)> Defaults =>
        Mappings.Select(m => (m.Key, ThemeColor.Parse(m.Default), m.Target));

    /// <summary>
    /// Moves the old colors into "My colors" (when one differs from its default), makes it the theme in use and saves
    /// the settings, which no longer hold the old keys.
    /// </summary>
    /// <returns>The theme made, or <c>null</c> when nothing had to move.</returns>
    public static Theme? Run(SettingsStore settings, ThemeStore store)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        if (settings.LegacyColors.Count == 0)
        {
            return null;
        }

        Theme light = store.Get(ThemeStore.LightId);
        Dictionary<string, ThemeEntry> entries = new(StringComparer.Ordinal);
        foreach ((string key, string defaultText, ThemeKey target, Part part) in Mappings)
        {
            if (!settings.LegacyColors.TryGetValue(key, out string? text)
                || !ThemeColor.TryParse(text, out ThemeColor color)
                || color == ThemeColor.Parse(defaultText))
            {
                continue;
            }

            ThemeEntry current = entries.TryGetValue(target.Id, out ThemeEntry? set)
                ? set
                : target.Kind == ThemeKeyKind.Color
                    ? ThemeEntry.Of(store.Resolver.Color(light, target))
                    : ThemeEntry.Of(store.Resolver.Attributes(light, target));
            entries[target.Id] = part switch
            {
                Part.Color => ThemeEntry.Of(color),
                Part.Foreground => ThemeEntry.Of(current.Attributes! with { Foreground = color }),
                Part.Background => ThemeEntry.Of(current.Attributes! with { Background = color }),
                _ => ThemeEntry.Of(current.Attributes! with { EffectColor = color }),
            };
        }

        Theme? theme = null;
        if (entries.Count > 0)
        {
            theme = store.Duplicate(ThemeStore.LightId, CoreStrings.Get("Theme_MigratedName"));
            theme = theme with { Entries = theme.Entries.SetItems(entries) };
            store.Save(theme);
            settings.UiThemeId = theme.Id;
        }

        settings.ForgetLegacyColors();
        settings.Save();
        return theme;
    }
}
