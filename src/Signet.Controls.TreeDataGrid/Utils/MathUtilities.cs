using System;

// In the root namespace (not .Utils) on purpose: every file of the fork finds this class before the
// "using Avalonia.Utilities;" directives, which import Avalonia's MathUtilities - internal since Avalonia 12.
namespace Signet.Controls.TreeDataGrid
{
    /// <summary>
    /// The floating point comparisons upstream used from <c>Avalonia.Utilities.MathUtilities</c> (MIT, the same
    /// tolerances), which is no longer public in Avalonia 12.
    /// </summary>
    internal static class MathUtilities
    {
        // Smallest double such that 1.0 + DoubleEpsilon != 1.0.
        private const double DoubleEpsilon = 2.2204460492503131e-016;

        /// <summary>Whether two values are equal within a relative tolerance.</summary>
        public static bool AreClose(double value1, double value2)
        {
            // In case they are Infinities (then epsilon check does not work).
            if (value1 == value2)
                return true;
            double eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * DoubleEpsilon;
            double delta = value1 - value2;
            return (-eps < delta) && (eps > delta);
        }

        /// <summary>Whether <paramref name="value1"/> is greater than <paramref name="value2"/> and not close to it.</summary>
        public static bool GreaterThan(double value1, double value2) => value1 > value2 && !AreClose(value1, value2);

        /// <summary>Whether a value is close to zero.</summary>
        public static bool IsZero(double value) => Math.Abs(value) < 10.0 * DoubleEpsilon;
    }
}
