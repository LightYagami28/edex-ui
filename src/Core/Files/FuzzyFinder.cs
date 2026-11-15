// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public sealed record FuzzyMatch(string FullPath, string RelativePath, int Score);

public sealed record FuzzySearchOptions
{
    private const int DefaultMaxResults = 50;
    private const int DefaultMaxDepth = 4;

    public int MaxResults { get; init; } = DefaultMaxResults;
    public int MaxDepth { get; init; } = DefaultMaxDepth;
    public bool ShowHidden { get; init; }
}

public static class FuzzyFinder
{
    private const int NoPrevious = -2;
    private const int NoMatch = -1;
    private const int ConsecutiveBonus = 5;
    private const int MatchBonus = 1;
    private const int WordStartBonus = 3;
    private const int LengthPenaltyDivisor = 8;

    public static int Score(string name, string query)
    {
        int score = 0;
        int position = 0;
        int previous = NoPrevious;
        foreach (var ch in query)
        {
            int found = name.IndexOf(ch, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return NoMatch;
            }

            score += Bonus(name, found, previous);
            previous = found;
            position = found + 1;
        }
        return score - name.Length / LengthPenaltyDivisor;
    }

    private static int Bonus(string name, int found, int previous) =>
        (found == previous + 1 ? ConsecutiveBonus : MatchBonus) + (IsWordStart(name, found) ? WordStartBonus : 0);

    private static bool IsWordStart(string name, int index) => index == 0 || name[index - 1] is '.' or '-' or '_' or ' ';

    public static IReadOnlyList<FuzzyMatch> Search(string root, string query) => Search(root, query, new FuzzySearchOptions());

    public static IReadOnlyList<FuzzyMatch> Search(string root, string query, FuzzySearchOptions options)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Matches(root, query, options)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.RelativePath.Length)
            .ThenBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxResults)
            .ToList();
    }

    private static IEnumerable<FuzzyMatch> Matches(string root, string query, FuzzySearchOptions options)
    {
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = options.MaxDepth, AttributesToSkip = 0 };
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", enumeration))
        {
            var relative = Path.GetRelativePath(root, path);
            int score = options.ShowHidden || !IsHiddenPath(relative) ? Score(Path.GetFileName(path), query) : NoMatch;
            if (score >= 0)
            {
                yield return new FuzzyMatch(path, relative, score);
            }
        }
    }

    private static bool IsHiddenPath(string relative) =>
        relative.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.'));
}
