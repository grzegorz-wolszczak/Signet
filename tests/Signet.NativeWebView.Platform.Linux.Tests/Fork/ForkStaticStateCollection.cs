namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// The tests that create backends under a UI context or handle <c>LinuxNativeCallbackExceptions.Caught</c>: both are
/// static state of the fork (the UI context of the caught exceptions, the event), so these tests do not run in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ForkStaticStateCollection
{
    public const string Name = "Fork static state";
}
