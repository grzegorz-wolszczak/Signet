using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Signet.App.Resources;

/// <summary>
/// XAML markup extension <c>{loc:Loc ResourceKey}</c> — inserts the
/// text from <see cref="Strings"/> at the place of use. For Avalonia properties it returns a binding to
/// <see cref="Localizer"/>, so the text changes live when the language is switched (no restart);
/// for plain CLR properties — just the text in the current language.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    /// <summary>Creates the extension without a key (to be set through the <see cref="Key"/> property).</summary>
    public LocExtension()
    {
    }

    /// <summary>Creates the extension with the key passed as a positional argument <c>{loc:Loc Key}</c>.</summary>
    public LocExtension(string key) => Key = key;

    /// <summary>Key in <c>Strings.resx</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetProperty: AvaloniaProperty })
        {
            return new ReflectionBinding("[" + Key + "]") { Source = Localizer.Instance, Mode = BindingMode.OneWay };
        }

        return Strings.Get(Key);
    }
}
