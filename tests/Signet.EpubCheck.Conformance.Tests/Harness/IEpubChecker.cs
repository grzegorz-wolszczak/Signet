using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;

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

/// <summary>The portion of the port that can check a complete OCF container and the single documents that have a checker.</summary>
internal sealed class OcfEpubChecker : IEpubChecker
{
    public void Check(CheckRequest request)
    {
        switch (request.Mode)
        {
            case CheckerMode.Epub:
                // Upstream's harness checks a whole publication with EpubCheck(file, report, profile): the version is not given
                // (the step "configured to check EPUB 3 rules" sets the version of the single-document checkers only).
                OCFChecker.Check(request.Path, EpubVersion.Unknown, request.Report, request.Profile);
                break;
            case CheckerMode.PackageDocument when request.Version == EpubVersion.Version2:
                ValidationContext context = new()
                {
                    Url = EpubCheckUrlUtils.ToUrl(request.Path),
                    Report = request.Report,
                    Culture = (request.Report as ReportBase)?.Culture,
                    Version = request.Version,
                    Profile = request.Profile,
                    MimeType = MediaTypes.PackageDocument,
                };
                new OPFChecker(context).Check();
                break;
            default:
                throw new ScenarioNotImplementedException("Checking this kind of single document is not implemented yet.");
        }
    }
}
