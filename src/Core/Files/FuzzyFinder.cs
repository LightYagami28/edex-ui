// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public sealed record FuzzyMatch(string FullPath, string RelativePath, int Score);

/// <summary>Fuzzy file name search under a folder: the query letters must appear in order in the name.</summary>
public static class FuzzyFinder
{
    /// <summary>Score of <paramref name="query"/> against <paramref name="name"/>, or -1 when it does not match. Higher is better.</summary>
    public static int Score(string name, string query)
    {
        int score = 0, position = 0, previous = -2;
        foreach (var ch in query)
        {
            int found = name.IndexOf(ch.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return -1;
            score += found == previous + 1 ? 5 : 1;
            if (found == 0 || name[found - 1] is '.' or '-' or '_' or ' ') score += 3;
            previous = found;
            position = found + 1;
        }
        return score - name.Length / 8;
    }

    public static IReadOnlyList<FuzzyMatch> Search(string root, string query, int maxResults = 50, int maxDepth = 4, bool showHidden = false)
    {
        var results = new List<FuzzyMatch>();
        if (!Directory.Exists(root)) return results;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = maxDepth, AttributesToSkip = 0 };
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
        {
            var relative = Path.GetRelativePath(root, path);
            if (!showHidden && relative.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.'))) continue;
            int score = Score(Path.GetFileName(path), query);
            if (score >= 0) results.Add(new FuzzyMatch(path, relative, score));
        }
        return results
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.RelativePath.Length)
            .ThenBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }
}
