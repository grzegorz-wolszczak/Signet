using AwesomeAssertions.Execution;
using AwesomeAssertions.Numeric;

namespace Signet.Controls.TreeDataGrid.Tests
{
    /// <summary>
    /// <see cref="IndexPath"/> is both an <c>IReadOnlyList&lt;int&gt;</c> and an <c>IComparable&lt;IndexPath&gt;</c>, so
    /// AwesomeAssertions' <c>Should()</c> overloads for the two are ambiguous; this more specific overload asserts on the
    /// path as a value (<see cref="IndexPath.Equals(IndexPath)"/>), like the upstream <c>Assert.Equal</c> did. The
    /// assertions are typed, so <c>Be(default)</c> and <c>Be(3)</c> mean <c>default(IndexPath)</c> and the implicit
    /// <c>int</c> to <see cref="IndexPath"/> conversion, as in <c>Assert.Equal&lt;IndexPath&gt;</c>.
    /// </summary>
    internal static class IndexPathAssertions
    {
        public static ComparableTypeAssertions<IndexPath> Should(this IndexPath value) =>
            new(value, AssertionChain.GetOrCreate());
    }
}
