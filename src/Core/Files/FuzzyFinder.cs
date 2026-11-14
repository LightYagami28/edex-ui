// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public sealed record FuzzyMatch(string FullPath, string RelativePath, int Score);

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
        int score = 0, position = 0, previous = NoPrevious;
        foreach (var ch in query)
        {
            int found = name.IndexOf(ch, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return NoMatch;
            }

            score += found == previous + 1 ? ConsecutiveBonus : MatchBonus;
            if (found == 0 || name[found - 1] is '.' or '-' or '_' or ' ')
            {
                score += WordStartBonus;
            }

            previous = found;
            position = found + 1;
        }
        return score - name.Length / LengthPenaltyDivisor;
    }

    public static IReadOnlyList<FuzzyMatch> Search(string root, string query, int maxResults = 50, int maxDepth = 4, bool showHidden = false)
    {
        var results = new List<FuzzyMatch>();
        if (!Directory.Exists(root))
        {
            return results;
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = maxDepth, AttributesToSkip = 0 };
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
        {
            var relative = Path.GetRelativePath(root, path);
            if (!showHidden && relative.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.')))
            {
                continue;
            }

            int score = Score(Path.GetFileName(path), query);
            if (score >= 0)
            {
                results.Add(new FuzzyMatch(path, relative, score));
            }
        }
        return results
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.RelativePath.Length)
            .ThenBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }
}
