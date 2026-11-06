// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Boot;

public sealed record BootLine(string Text, int DelayMs);

/// <summary>Turns the boot log text into timed lines for the start-up screen. The timing is deterministic.</summary>
public static class BootSequence
{
    private const int greetingDelayMs = 400;
    private const int pauseEveryLines = 17;
    private const int pauseDelayMs = 180;
    private const int fastDelayBaseMs = 8;
    private const int fastDelayStep = 7;
    private const int fastDelaySpread = 40;

    public static IReadOnlyList<BootLine> Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<BootLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            // The greeting waits longer; the rest scrolls fast with a pause every 17th line.
            int delay = greetingDelayMs;
            if (i != 0)
            {
                delay = i % pauseEveryLines == 0
                    ? pauseDelayMs
                    : fastDelayBaseMs + (i * fastDelayStep % fastDelaySpread);
            }

            result.Add(new BootLine(lines[i], delay));
        }
        return result;
    }

    public static int TotalMs(IEnumerable<BootLine> lines) => lines.Sum(l => l.DelayMs);
}
