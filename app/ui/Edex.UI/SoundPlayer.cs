// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using Edex.Core.Audio;

namespace Edex.UI;

/// <summary>
/// Plays the synthesized effects. Windows uses winmm PlaySound from memory; Linux and macOS hand a cached WAV file to
/// the system player (pw-play, paplay, aplay or afplay). Missing audio never raises errors.
/// </summary>
public sealed class SoundPlayer
{
    private readonly Dictionary<SoundEffect, byte[]> _sounds = [];
    private readonly Dictionary<SoundEffect, GCHandle> _pinned = [];
    private readonly Dictionary<SoundEffect, string> _files = [];
    private readonly string? _player;
    private long _lastClick;

    public bool Enabled { get; set; }

    public SoundPlayer(bool enabled, double volume)
    {
        Enabled = enabled;
        if (!enabled) return;
        foreach (var effect in Enum.GetValues<SoundEffect>())
            _sounds[effect] = SoundSynth.Create(effect, effect == SoundEffect.KeyClick ? volume * 0.5 : volume);
        if (!OperatingSystem.IsWindows()) _player = FindPlayer();
    }

    /// <summary>Name of the backend in use (for the smoke-test report).</summary>
    public string Backend => !Enabled ? "off" : OperatingSystem.IsWindows() ? "winmm" : _player is null ? "none" : Path.GetFileName(_player);

    public void KeyClick()
    {
        // Spawning a player per key is too heavy on Unix; throttle there.
        long now = Environment.TickCount64;
        if (!OperatingSystem.IsWindows() && now - _lastClick < 60) return;
        _lastClick = now;
        Play(SoundEffect.KeyClick);
    }

    public void Play(SoundEffect effect)
    {
        if (!Enabled || !_sounds.TryGetValue(effect, out var wav)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!_pinned.TryGetValue(effect, out var handle))
                    _pinned[effect] = handle = GCHandle.Alloc(wav, GCHandleType.Pinned); // PlaySound reads the buffer asynchronously
                PlaySoundW(handle.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                return;
            }
            if (_player is null) return;
            if (!_files.TryGetValue(effect, out var file))
            {
                var dir = Path.Combine(Path.GetTempPath(), $"edex-ui-sounds-{Environment.UserName}");
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, effect + ".wav");
                File.WriteAllBytes(file, wav);
                _files[effect] = file;
            }
            var psi = new ProcessStartInfo(_player) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            if (Path.GetFileName(_player) == "aplay") psi.ArgumentList.Add("-q");
            psi.ArgumentList.Add(file);
            using var process = Process.Start(psi);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            Enabled = false;
        }
    }

    private static string? FindPlayer()
    {
        string[] candidates = OperatingSystem.IsMacOS() ? ["afplay"] : ["pw-play", "paplay", "aplay"];
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/bin", "/bin", "/usr/local/bin"]);
        foreach (var name in candidates)
            foreach (var dir in paths)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        return null;
    }

    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);
}
