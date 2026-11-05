using System;
using System.Globalization;
using System.Text;
using Signet.EpubCheck.Core.Ocf;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>Steps that exercise the OCF file name checker.</summary>
internal static class FilenameSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((world, arguments) => Check(world, arguments.Get<string>(0)), "checking file name {string}");
        steps.Add((world, arguments) => Check(world, new Rune(int.Parse(arguments.Get<string>(0).AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString()),
            "checking file name containing code point {codepoint}");
    }

    private static void Check(ScenarioWorld world, string name) =>
        OCFFilenameChecker.Check(name, world.Version, world.Report);
}
