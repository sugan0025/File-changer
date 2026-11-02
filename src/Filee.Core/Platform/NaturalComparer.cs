// Orders names the way people count: comic book pages, files to merge.

namespace Filee.Core.Platform;

/// <summary>
/// Compares names the way people number pages and files: digit runs by value ("p2" &lt; "p10"), case-insensitively,
/// with zero-padding tie breaking ("p01" vs "p1") and zero heap allocations.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.CompareOrdinal(x, y);

        var spanX = x.AsSpan();
        var spanY = y.AsSpan();

        int i = 0, j = 0;
        int leadingZeroTie = 0;

        while (i < spanX.Length && j < spanY.Length)
        {
            if (char.IsAsciiDigit(spanX[i]) && char.IsAsciiDigit(spanY[j]))
            {
                var startX = i;
                var startY = j;
                while (i < spanX.Length && char.IsAsciiDigit(spanX[i]))
                    i++;
                while (j < spanY.Length && char.IsAsciiDigit(spanY[j]))
                    j++;

                var numX = spanX[startX..i].TrimStart('0');
                var numY = spanY[startY..j].TrimStart('0');

                var byValue = numX.Length != numY.Length
                    ? numX.Length.CompareTo(numY.Length)
                    : numX.SequenceCompareTo(numY);

                if (byValue != 0)
                    return byValue;

                if (leadingZeroTie == 0)
                {
                    // Full digit run length difference accounts for leading zeros
                    leadingZeroTie = (i - startX).CompareTo(j - startY);
                }
                continue;
            }

            var byChar = char.ToLowerInvariant(spanX[i]).CompareTo(char.ToLowerInvariant(spanY[j]));
            if (byChar != 0)
                return byChar;

            i++;
            j++;
        }

        var byRemaining = (spanX.Length - i).CompareTo(spanY.Length - j);
        if (byRemaining != 0)
            return byRemaining;

        return leadingZeroTie != 0 ? leadingZeroTie : string.CompareOrdinal(x, y);
    }
}
