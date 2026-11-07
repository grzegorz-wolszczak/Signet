using System.Globalization;
using System.Runtime.CompilerServices;
using Signet.Core.Tests.TestSupport;
using Xunit;

[assembly: AssemblyFixture(typeof(TempCleanupGuard))]

namespace Signet.EpubCheck.Core.Tests;

/// <summary>
/// Gives the test process its own temporary and data folders before any test runs (<see cref="TestIsolation"/>), and English
/// texts: the tests compare the texts of the messages, which follow the language of the machine unless a test asks for one.
/// </summary>
internal static class TestIsolationSetup
{
    [ModuleInitializer]
    internal static void Install()
    {
        TestIsolation.Install("Signet.EpubCheck.Core.Tests");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }
}
