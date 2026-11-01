// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Boot;

public sealed record BootLine(string Text, int DelayMs);

/// <summary>Turns the boot log text into timed lines for the start-up screen. The timing is deterministic.</summary>
public static class BootSequence
{
    public static IReadOnlyList<BootLine> Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<BootLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            // The greeting waits longer; the rest scrolls fast with a pause every 17th line.
            int delay = i == 0 ? 400 : i % 17 == 0 ? 180 : 8 + i * 7 % 40;
            result.Add(new BootLine(lines[i], delay));
        }
        return result;
    }

    public static int TotalMs(IEnumerable<BootLine> lines) => lines.Sum(l => l.DelayMs);
}
