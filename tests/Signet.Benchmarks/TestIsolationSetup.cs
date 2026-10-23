using System.Runtime.CompilerServices;
using Signet.Core.Tests.TestSupport;
using Xunit;

[assembly: AssemblyFixture(typeof(TempCleanupGuard))]

namespace Signet.Benchmarks;

/// <summary>Gives the benchmark process its own temporary and data folders before anything runs (<see cref="TestIsolation"/>).</summary>
internal static class TestIsolationSetup
{
    [ModuleInitializer]
    internal static void Install() => TestIsolation.Install("Signet.Benchmarks");
}
