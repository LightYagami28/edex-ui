// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Media;
using Edex.Core.Files;

namespace Edex.UI;

/// <summary>Per-OS tweaks supplied by the platform projects (Windows, Linux, macOS).</summary>
public sealed record PlatformProfile
{
    public required string Name { get; init; }

    /// <summary>Modifier used for app shortcuts that must not collide with the shell (Ctrl+Shift on Win/Linux, Cmd on macOS).</summary>
    public bool UseCommandKey { get; init; }

    public string PreferredMonospaceFonts { get; init; } = "Cascadia Mono, Consolas, monospace";

    /// <summary>Optional native file icons (full path, kind) supplied by the OS; null falls back to the built-in vector icons.</summary>
    public Func<string, FileEntryKind, IImage?>? FileIcons { get; init; }
}
