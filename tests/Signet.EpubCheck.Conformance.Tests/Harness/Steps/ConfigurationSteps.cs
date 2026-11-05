using System.Globalization;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The steps that set the environment and the configuration of the checker (upstream's <c>TestEnvironment</c> and <c>EPUBCheckConfiguration</c>).</summary>
internal static class ConfigurationSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((w, a) => w.BasePath = a.Get<string>(0), "(the )(EPUB )test files (are)located at {string}");
        steps.Add((w, a) => w.DefaultCulture = a.Get<CultureInfo>(0), "(the )default locale (is )(set to )('){locale}(')");
        steps.Add((_, _) => { }, "EPUBCheck with default settings");
        steps.Add((w, a) => w.Version = a.Get<EpubVersion>(0), "EPUBCheck configured to check EPUB {version} rules");
        steps.Add((w, a) => w.Report.MaxOfEachMessage = a.Get<int>(0), "the maximum number of each message is set to {int}");
        steps.Add((w, a) => w.Mode = a.Get<CheckerMode>(0), "EPUBCheck configured to check a(n) {checkerMode}");
        steps.Add((w, a) => w.Profile = a.Get<EpubProfile>(0), "EPUBCheck configured with the ('){profile}(') profile");
        steps.Add((w, a) => w.Report.Format = a.Get<string>(0), "(the )reporting format (is )set to {}");
        steps.Add((w, a) => w.Report.ReportingLevel = a.Get<Severity>(0), "(the )reporting level (is )set to {severity}");
        steps.Add((w, a) => w.Report.SetLocale(a.Get<CultureInfo>(0)), "(the )reporting locale (is )set to ('){locale}(')");
    }
}
