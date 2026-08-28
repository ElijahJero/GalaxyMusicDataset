namespace GalaxyMusicDataset.Services.Normalization;

public static class StringSimilarity
{
    public static double Ratio(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var lev = 1d - (Levenshtein(a, b) / (double)Math.Max(a.Length, b.Length));
        var token = TokenJaccard(a, b);
        var best = Math.Max(lev, token);

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            var containment = Math.Min(a.Length, b.Length) / (double)Math.Max(a.Length, b.Length);
            best = Math.Max(best, 0.75 + (0.25 * containment));
        }

        return Math.Clamp(best, 0, 1);
    }

    public static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    private static double TokenJaccard(string a, string b)
    {
        var left = a.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var right = b.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var set = new HashSet<string>(left, StringComparer.Ordinal);
        var intersection = 0;
        foreach (var token in right)
        {
            if (set.Contains(token))
            {
                intersection++;
            }
        }

        var union = set.Count;
        foreach (var token in right)
        {
            set.Add(token);
        }

        union = set.Count;
        return union == 0 ? 0 : intersection / (double)union;
    }
}
