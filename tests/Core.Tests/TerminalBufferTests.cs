// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

[TestClass]
public class TerminalBufferTests
{
    [TestMethod]
    public void PrintsTextAndHandlesNewlines()
    {
        var buffer = new TerminalBuffer(20, 5);
        buffer.Feed("hello\r\nworld");
        var snap = buffer.Snapshot();
        Assert.AreEqual("hello", snap.LineText(0));
        Assert.AreEqual("world", snap.LineText(1));
        Assert.AreEqual(5, snap.CursorX);
        Assert.AreEqual(1, snap.CursorY);
    }

    [TestMethod]
    public void WrapsAtRightMarginAndScrollsIntoScrollback()
    {
        var buffer = new TerminalBuffer(4, 2);
        buffer.Feed("abcdefgh\r\nij");
        var snap = buffer.Snapshot();
        Assert.AreEqual("efgh", snap.LineText(0));
        Assert.AreEqual("ij", snap.LineText(1));
        Assert.AreEqual(1, snap.ScrollbackLength);
        buffer.ScrollViewport(1);
        Assert.AreEqual("abcd", buffer.Snapshot().LineText(0));
    }

    [TestMethod]
    public void CursorPositioningAndEraseLine()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("0123456789\x1b[1;4H\x1b[K");
        Assert.AreEqual("012", buffer.Snapshot().LineText(0));
        buffer.Feed("\x1b[2J\x1b[3;2HX");
        var snap = buffer.Snapshot();
        Assert.AreEqual("", snap.LineText(0));
        Assert.AreEqual(" X", snap.LineText(2));
    }

    [TestMethod]
    public void ParsesSgrColors()
    {
        var buffer = new TerminalBuffer(10, 2);
        buffer.Feed("\x1b[1;31mA\x1b[38;5;200mB\x1b[38;2;1;2;3;48;5;4mC\x1b[0mD");
        var line = buffer.Snapshot().Lines[0];
        Assert.AreEqual(TerminalCell.Palette(1), line[0].Foreground);
        Assert.IsTrue(line[0].Attributes.HasFlag(CellAttributes.Bold));
        Assert.AreEqual(TerminalCell.Palette(200), line[1].Foreground);
        Assert.AreEqual(TerminalCell.Rgb(1, 2, 3), line[2].Foreground);
        Assert.AreEqual(TerminalCell.Palette(4), line[2].Background);
        Assert.AreEqual(0u, line[3].Foreground);
        Assert.AreEqual(CellAttributes.None, line[3].Attributes);
    }

    [TestMethod]
    public void AlternateScreenRestoresMainScreen()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("main");
        buffer.Feed("\x1b[?1049h");
        Assert.IsTrue(buffer.IsAlternateScreen);
        buffer.Feed("alt");
        Assert.AreEqual("alt", buffer.Snapshot().LineText(0));
        buffer.Feed("\x1b[?1049l");
        Assert.IsFalse(buffer.IsAlternateScreen);
        var snap = buffer.Snapshot();
        Assert.AreEqual("main", snap.LineText(0));
        Assert.AreEqual(4, snap.CursorX);
    }

    [TestMethod]
    public void RepliesToCursorPositionReport()
    {
        var buffer = new TerminalBuffer(10, 3);
        string? reply = null;
        buffer.Response += r => reply = r;
        buffer.Feed("ab\x1b[6n");
        Assert.AreEqual("\x1b[1;3R", reply);
    }

    [TestMethod]
    public void ReadsTitleAndModes()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("\x1b]0;my title\x07\x1b[?1h\x1b[?2004h\x1b[?25l");
        Assert.AreEqual("my title", buffer.Title);
        Assert.IsTrue(buffer.ApplicationCursorKeys);
        Assert.IsTrue(buffer.BracketedPaste);
        Assert.IsFalse(buffer.CursorVisible);
    }

    [TestMethod]
    public void DecodesUtf8SplitAcrossChunks()
    {
        var buffer = new TerminalBuffer(10, 2);
        var bytes = System.Text.Encoding.UTF8.GetBytes("è€");
        buffer.Feed(bytes.AsSpan(0, 1));
        buffer.Feed(bytes.AsSpan(1, 3));
        buffer.Feed(bytes.AsSpan(4));
        Assert.AreEqual("è€", buffer.Snapshot().LineText(0));
    }

    [TestMethod]
    public void ScrollRegionKeepsLinesOutsideRegion()
    {
        var buffer = new TerminalBuffer(5, 4);
        buffer.Feed("top\r\n1\r\n2\r\nbot");
        buffer.Feed("\x1b[2;3r\x1b[3;1H\n");
        var snap = buffer.Snapshot();
        Assert.AreEqual("top", snap.LineText(0));
        Assert.AreEqual("2", snap.LineText(1));
        Assert.AreEqual("", snap.LineText(2));
        Assert.AreEqual("bot", snap.LineText(3));
    }

    [TestMethod]
    public void ResizeKeepsContent()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.Feed("a\r\nb\r\nc\r\nd");
        buffer.Resize(5, 2);
        var snap = buffer.Snapshot();
        Assert.AreEqual(5, snap.Columns);
        Assert.AreEqual("c", snap.LineText(0));
        Assert.AreEqual("d", snap.LineText(1));
        buffer.Resize(20, 6);
        Assert.AreEqual(6, buffer.Snapshot().Rows);
    }
}
