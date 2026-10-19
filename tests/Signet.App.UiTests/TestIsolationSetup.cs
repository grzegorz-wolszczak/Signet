using System.Runtime.CompilerServices;
using Signet.Core.Tests.TestSupport;
using Xunit;

[assembly: AssemblyFixture(typeof(TempCleanupGuard))]

namespace Signet.App.UiTests;

/// <summary>Gives the test process its own temporary and data folders before any test runs (<see cref="TestIsolation"/>).</summary>
internal static class TestIsolationSetup
{
    [ModuleInitializer]
    internal static void Install() => TestIsolation.Install("Signet.App.UiTests");
}
