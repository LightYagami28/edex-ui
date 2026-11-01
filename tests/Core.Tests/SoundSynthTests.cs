// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Audio;

namespace Edex.Core.Tests;

[TestClass]
public class SoundSynthTests
{
    [TestMethod]
    [DataRow(SoundEffect.KeyClick)]
    [DataRow(SoundEffect.Startup)]
    [DataRow(SoundEffect.Info)]
    [DataRow(SoundEffect.Alert)]
    [DataRow(SoundEffect.PanelOpen)]
    [DataRow(SoundEffect.Exit)]
    public void ProducesValidPcmWav(SoundEffect effect)
    {
        var wav = SoundSynth.Create(effect, 0.8);
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.AreEqual(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.AreEqual(1, BitConverter.ToInt16(wav, 20));            // PCM
        Assert.AreEqual(SoundSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        int dataBytes = BitConverter.ToInt32(wav, 40);
        Assert.AreEqual(wav.Length - 44, dataBytes);
        Assert.IsTrue(dataBytes > 0);
        short peak = 0;
        for (int i = 44; i < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wav, i)));
        Assert.IsTrue(peak >= 1000 && peak <= short.MaxValue);                // audible and not clipped beyond range
    }

    [TestMethod]
    public void VolumeZeroIsSilent()
    {
        var wav = SoundSynth.Create(SoundEffect.Startup, 0);
        for (int i = 44; i < wav.Length; i += 2) Assert.AreEqual(0, BitConverter.ToInt16(wav, i));
    }
}
