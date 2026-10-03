namespace Signet.App.Resources;

/// <summary>An object that recomputes its texts after a language change (see <see cref="Strings.RegisterLanguageAware"/>).</summary>
public interface ILanguageAware
{
    /// <summary>Called after the UI language is switched.</summary>
    void OnLanguageChanged();
}
