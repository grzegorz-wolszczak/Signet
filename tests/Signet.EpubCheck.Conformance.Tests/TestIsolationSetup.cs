using System.Runtime.CompilerServices;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Xunit;

[assembly: AssemblyFixture(typeof(TempCleanupGuard))]
[assembly: AssemblyFixture(typeof(ConformanceReportWriter))]

namespace Signet.EpubCheck.Conformance.Tests;

/// <summary>Gives the test process its own temporary and data folders before any test runs (<see cref="TestIsolation"/>).</summary>
internal static class TestIsolationSetup
{
    [ModuleInitializer]
    internal static void Install() => TestIsolation.Install("Signet.EpubCheck.Conformance.Tests");
}
