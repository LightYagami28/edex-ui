// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Audio;

namespace Edex.Core.Tests;

public class SoundSynthTests
{
    [Theory]
    [InlineData(SoundEffect.KeyClick)]
    [InlineData(SoundEffect.Startup)]
    [InlineData(SoundEffect.Info)]
    [InlineData(SoundEffect.Alert)]
    [InlineData(SoundEffect.PanelOpen)]
    [InlineData(SoundEffect.Exit)]
    public void ProducesValidPcmWav(SoundEffect effect)
    {
        var wav = SoundSynth.Create(effect, 0.8);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));            // PCM
        Assert.Equal(SoundSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        int dataBytes = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataBytes);
        Assert.True(dataBytes > 0);
        short peak = 0;
        for (int i = 44; i < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wav, i)));
        Assert.InRange(peak, 1000, short.MaxValue);                // audible and not clipped beyond range
    }

    [Fact]
    public void VolumeZeroIsSilent()
    {
        var wav = SoundSynth.Create(SoundEffect.Startup, 0);
        for (int i = 44; i < wav.Length; i += 2) Assert.Equal(0, BitConverter.ToInt16(wav, i));
    }
}
