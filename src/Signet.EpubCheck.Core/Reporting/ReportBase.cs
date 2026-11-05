using System;
using System.Globalization;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// The part of a report every kind shares (upstream's <c>MasterReport</c>): it looks the message up, applies the
/// reporting level, counts what was reported and hands it to <see cref="OnMessage"/>.
/// </summary>
public abstract class ReportBase : IReport
{
    private Severity _reportingLevel = Severity.Info;

    /// <summary>Creates a report with the default messages in the language of <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    protected ReportBase()
        : this(new LocalizedMessageDictionary())
    {
    }

    /// <summary>Creates a report that gets its messages from <paramref name="dictionary"/>.</summary>
    protected ReportBase(IMessageDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Dictionary = dictionary;
    }

    /// <summary>
    /// Switches the messages to the language of <paramref name="culture"/> (English or Polish; any other gets English).
    /// Drops the overrides of a custom messages file, as upstream does.
    /// </summary>
    public void SetLocale(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Dictionary = new LocalizedMessageDictionary(culture);
    }

    /// <summary>
    /// Takes the severities, texts and suggestions from a custom messages file (<c>--customMessages</c>); the problems of
    /// the file are reported to this report with the dictionary it had before. The texts that the file does not
    /// override are in <paramref name="culture"/>'s language.
    /// </summary>
    public void SetOverrideFile(string overrideFile, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(overrideFile);
        Dictionary = new OverriddenMessageDictionary(overrideFile, this, culture);
    }

    /// <inheritdoc />
    /// <remarks>Settable so that the language or a custom messages file can change the dictionary after the report was made.</remarks>
    public IMessageDictionary Dictionary { get; set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The level is not between <see cref="Severity.Usage"/> and <see cref="Severity.Fatal"/>.</exception>
    public Severity ReportingLevel
    {
        get => _reportingLevel;
        set
        {
            // Upstream prints a message and keeps the old level; the tool only sets levels that are valid.
            if (value < Severity.Usage || value > Severity.Fatal)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }

            _reportingLevel = value;
        }
    }

    /// <inheritdoc />
    public int MaxOfEachMessage { get; set; } = IReport.DefaultMaxOfEachMessage;

    /// <inheritdoc />
    public string? EpubFileName { get; set; }

    /// <inheritdoc />
    public string? CustomMessageFile { get; set; }

    /// <inheritdoc />
    public int ErrorCount { get; private set; }

    /// <inheritdoc />
    public int WarningCount { get; private set; }

    /// <inheritdoc />
    public int FatalErrorCount { get; private set; }

    /// <inheritdoc />
    public int InfoCount { get; private set; }

    /// <inheritdoc />
    public int UsageCount { get; private set; }

    /// <inheritdoc />
    public void Message(MessageId id, EpubLocation location, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(location);
        Message message = Dictionary.GetMessage(id);
        if (message.Severity < ReportingLevel)
        {
            return;
        }

        switch (message.Severity)
        {
            case Severity.Error:
                ErrorCount++;
                break;
            case Severity.Warning:
                WarningCount++;
                break;
            case Severity.Fatal:
                FatalErrorCount++;
                break;
            case Severity.Usage:
                UsageCount++;
                break;
            case Severity.Info:
                InfoCount++;
                break;
        }

        OnMessage(message, location, args);
    }

    /// <inheritdoc />
    public abstract void Info(string? resource, FeatureKind feature, string? value);

    /// <inheritdoc />
    public virtual void Initialize()
    {
    }

    /// <inheritdoc />
    public virtual int Generate() => 0;

    /// <summary>Receives a message that passed the reporting level (and is already counted).</summary>
    protected abstract void OnMessage(Message message, EpubLocation location, object?[] args);
}
