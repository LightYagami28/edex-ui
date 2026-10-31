// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Audio;

public enum SoundEffect { KeyClick, Startup, Info, Alert, PanelOpen, Exit }

/// <summary>Synthesizes the interface sound effects as 16-bit mono PCM WAV files (no audio assets needed).</summary>
public static class SoundSynth
{
    public const int SampleRate = 22050;

    public static byte[] Create(SoundEffect effect, double volume = 0.5)
    {
        volume = Math.Clamp(volume, 0, 1);
        float[] samples = effect switch
        {
            SoundEffect.KeyClick => Click(),
            SoundEffect.Startup => Sequence((392, 0.09), (523.25, 0.09), (659.25, 0.09), (783.99, 0.28)),
            SoundEffect.Info => Sequence((880, 0.06), (1318.5, 0.1)),
            SoundEffect.Alert => Sequence((587.33, 0.12), (0, 0.05), (440, 0.18)),
            SoundEffect.PanelOpen => Sweep(300, 1200, 0.18),
            SoundEffect.Exit => Sequence((659.25, 0.08), (523.25, 0.08), (392, 0.2)),
            _ => [],
        };
        return ToWav(samples, volume);
    }

    /// <summary>Short mechanical click: a noise transient plus a damped high tone.</summary>
    private static float[] Click()
    {
        int n = (int)(SampleRate * 0.03);
        var data = new float[n];
        uint seed = 0x9E3779B9;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            seed = seed * 1664525 + 1013904223;
            double noise = ((seed >> 9) / (double)(1 << 23)) * 2 - 1;
            double env = Math.Exp(-t * 400);
            double tone = Math.Sin(2 * Math.PI * 2600 * t) * Math.Exp(-t * 180);
            data[i] = (float)(0.5 * noise * env + 0.5 * tone);
        }
        return data;
    }

    /// <summary>Notes (frequency in Hz, seconds; 0 Hz = rest) with a soft square-ish timbre and short envelopes.</summary>
    private static float[] Sequence(params (double Frequency, double Seconds)[] notes)
    {
        var data = new List<float>();
        foreach (var (frequency, seconds) in notes)
        {
            int n = (int)(SampleRate * seconds);
            for (int i = 0; i < n; i++)
            {
                if (frequency <= 0) { data.Add(0); continue; }
                double t = (double)i / SampleRate;
                double env = Math.Min(1, t / 0.005) * Math.Exp(-t * 6 / seconds);
                double phase = 2 * Math.PI * frequency * t;
                double wave = Math.Sin(phase) + 0.3 * Math.Sin(3 * phase) / 3 + 0.15 * Math.Sin(5 * phase) / 5;
                data.Add((float)(wave * env * 0.8));
            }
        }
        return [.. data];
    }

    private static float[] Sweep(double from, double to, double seconds)
    {
        int n = (int)(SampleRate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double frequency = from * Math.Pow(to / from, t);
            phase += 2 * Math.PI * frequency / SampleRate;
            double env = Math.Sin(Math.PI * t);
            data[i] = (float)(Math.Sin(phase) * env * 0.6);
        }
        return data;
    }

    public static byte[] ToWav(float[] samples, double volume)
    {
        int dataBytes = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
            w.Write((short)Math.Clamp(Math.Round(s * volume * short.MaxValue), short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
