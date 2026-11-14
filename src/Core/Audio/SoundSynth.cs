// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Audio;

public enum SoundEffect { KeyClick, Startup, Info, Alert, PanelOpen, Exit }

public static class SoundSynth
{
    public const int SampleRate = 22050;

    private const int HeaderSize = 44;
    private const int RiffChunkSizeOffset = 36;
    private const int FormatChunkSize = 16;
    private const int PcmFormat = 1;
    private const int MonoChannels = 1;
    private const int BytesPerSample = 2;
    private const int BitsPerSample = 16;

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
            _ => Sequence((659.25, 0.08), (523.25, 0.08), (392, 0.2)),
        };
        return ToWav(samples, volume);
    }

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
        int dataBytes = samples.Length * BytesPerSample;
        using var ms = new MemoryStream(HeaderSize + dataBytes);
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8);
        w.Write(RiffChunkSizeOffset + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(FormatChunkSize);
        w.Write((short)PcmFormat);
        w.Write((short)MonoChannels);
        w.Write(SampleRate);
        w.Write(SampleRate * BytesPerSample);
        w.Write((short)BytesPerSample);
        w.Write((short)BitsPerSample);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write((short)Math.Clamp(Math.Round(s * volume * short.MaxValue), short.MinValue, short.MaxValue));
        }

        w.Flush();
        return ms.ToArray();
    }
}
