// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Pty;

/// <summary>Options used to start a shell attached to a pseudo terminal.</summary>
public sealed record PtyOptions
{
    public required string Shell { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public int Columns { get; init; } = 80;
    public int Rows { get; init; } = 24;
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>A running child process connected to a pseudo terminal.</summary>
public interface IPtyConnection : IDisposable
{
    int ProcessId { get; }

    /// <summary>Bytes written by the child (terminal output).</summary>
    Stream Output { get; }

    /// <summary>Bytes sent to the child (keyboard input).</summary>
    Stream Input { get; }

    void Resize(int columns, int rows);

    /// <summary>Exit code once the child has terminated, otherwise null.</summary>
    int? ExitCode { get; }

    /// <summary>Raised once with the exit code when the child terminates.</summary>
    event EventHandler<int>? Exited;
}

public static class PtyFactory
{
    public static IPtyConnection Spawn(PtyOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Shell);
        if (OperatingSystem.IsWindows()) return WindowsPty.Spawn(options);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return UnixPty.Spawn(options);
        throw new PlatformNotSupportedException("No pseudo terminal backend for this operating system.");
    }

    internal static Dictionary<string, string> BuildEnvironment(IReadOnlyDictionary<string, string>? extra)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var env = new Dictionary<string, string>(comparer);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.Length > 0) env[key] = entry.Value as string ?? "";
        }
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        env["TERM_PROGRAM"] = "eDEX-UI";
        if (extra is not null)
        {
            foreach (var (key, value) in extra) env[key] = value;
        }
        return env;
    }
}
