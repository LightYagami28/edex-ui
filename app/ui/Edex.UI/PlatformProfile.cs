// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.UI;

/// <summary>Per-OS tweaks supplied by the platform projects (Windows, Linux, macOS).</summary>
public sealed record PlatformProfile
{
    public required string Name { get; init; }

    /// <summary>Modifier used for app shortcuts that must not collide with the shell (Ctrl+Shift on Win/Linux, Cmd on macOS).</summary>
    public bool UseCommandKey { get; init; }

    public string PreferredMonospaceFonts { get; init; } = "Cascadia Mono, Consolas, monospace";
}
