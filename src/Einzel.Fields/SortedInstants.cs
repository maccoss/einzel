namespace Einzel.Fields;

/// <summary>Bisection over increasing instants: the one search every switch lookup shares.</summary>
/// <remarks>
/// Written so that a NaN instant compares as past every entry - the answer a front-to-back scan
/// for the first entry it falls short of gives - so swapping a scan for this changes no result.
/// </remarks>
internal static class SortedInstants
{
    /// <summary>The index of the first entry strictly after an instant, or the count when there is none.</summary>
    /// <param name="instants">Increasing instants.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The index.</returns>
    public static int FirstAfter(ReadOnlySpan<double> instants, double timeSeconds)
    {
        var low = 0;
        var high = instants.Length;

        while (low < high)
        {
            var middle = (low + high) >>> 1;

            if (instants[middle] > timeSeconds)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    /// <summary>The index of the first entry at or after an instant, or the count when there is none.</summary>
    /// <param name="instants">Increasing instants.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The index.</returns>
    public static int FirstAtOrAfter(ReadOnlySpan<double> instants, double timeSeconds)
    {
        var low = 0;
        var high = instants.Length;

        while (low < high)
        {
            var middle = (low + high) >>> 1;

            if (instants[middle] >= timeSeconds)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }
}
