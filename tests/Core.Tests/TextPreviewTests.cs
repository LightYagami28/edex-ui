// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Files;

namespace Edex.Core.Tests;

[TestClass]
public class TextPreviewTests
{
    [TestMethod]
    public void DecodesUtf8WithAndWithoutBom()
    {
        Assert.AreEqual("città", TextPreview.Decode(Encoding.UTF8.GetBytes("città")));
        Assert.AreEqual("città", TextPreview.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("città")]));
    }

    [TestMethod]
    public void DecodesUtf16ByBom()
    {
        Assert.AreEqual("ok è", TextPreview.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("ok è")]));
        Assert.AreEqual("ok è", TextPreview.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("ok è")]));
    }

    [TestMethod]
    public void BinaryBecomesHexDump()
    {
        var dump = TextPreview.Decode([0x7F, 0x45, 0x4C, 0x46, 0x00, 0x01]);
        Assert.StartsWith("00000000  7f 45 4c 46 00 01", dump);
    }
}
