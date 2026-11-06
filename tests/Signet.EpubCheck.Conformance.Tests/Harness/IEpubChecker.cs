using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>What a "checking …" step asks the checker to check.</summary>
/// <param name="Path">The full path of the publication, the folder of an expanded one, or the single document.</param>
/// <param name="Mode">What the path is.</param>
/// <param name="Version">The EPUB version to check against (a single document has no version of its own).</param>
/// <param name="Profile">The validation profile.</param>
/// <param name="Report">Where the checker reports.</param>
internal sealed record CheckRequest(string Path, CheckerMode Mode, EpubVersion Version, EpubProfile Profile, IReport Report);

/// <summary>The checker the scenarios exercise. The port's checker is plugged in here as it grows.</summary>
internal interface IEpubChecker
{
    void Check(CheckRequest request);
}

/// <summary>The checker of the port, which is not there yet: every check says so.</summary>
internal sealed class UnavailableEpubChecker : IEpubChecker
{
    public void Check(CheckRequest request) =>
        throw new ScenarioNotImplementedException("The EPUB checker of the port is not implemented yet.");
}

/// <summary>The portion of the port that can check a complete OCF ZIP container.</summary>
internal sealed class OcfEpubChecker : IEpubChecker
{
    public void Check(CheckRequest request)
    {
        if (request.Mode != CheckerMode.Epub)
        {
            throw new ScenarioNotImplementedException("Checking individual documents is not implemented yet.");
        }

        OCFChecker.Check(request.Path, request.Version, request.Report);
    }
}
