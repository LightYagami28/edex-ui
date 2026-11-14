// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Pty;

public sealed record PtyOptions
{
    public required string Shell { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public int Columns { get; init; } = 80;
    public int Rows { get; init; } = 24;
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

public interface IPtyConnection : IDisposable
{
    int ProcessId { get; }

    Stream Output { get; }

    Stream Input { get; }

    void Resize(int columns, int rows);

    int? ExitCode { get; }

    event EventHandler<int>? Exited;
}

public static class PtyFactory
{
    public static IPtyConnection Spawn(PtyOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Shell);
        return OperatingSystem.IsWindows() ? WindowsPty.Spawn(options) : UnixPty.Spawn(options);
    }

    internal static Dictionary<string, string> BuildEnvironment(IReadOnlyDictionary<string, string>? extra)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var env = new Dictionary<string, string>(comparer);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.Length > 0)
            {
                env[key] = entry.Value as string ?? "";
            }
        }
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        env["TERM_PROGRAM"] = "eDEX-UI";
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                env[key] = value;
            }
        }
        return env;
    }
}
