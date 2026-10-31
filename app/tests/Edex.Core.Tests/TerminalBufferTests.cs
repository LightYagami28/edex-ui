// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

public class TerminalBufferTests
{
    [Fact]
    public void PrintsTextAndHandlesNewlines()
    {
        var buffer = new TerminalBuffer(20, 5);
        buffer.Feed("hello\r\nworld");
        var snap = buffer.Snapshot();
        Assert.Equal("hello", snap.LineText(0));
        Assert.Equal("world", snap.LineText(1));
        Assert.Equal(5, snap.CursorX);
        Assert.Equal(1, snap.CursorY);
    }

    [Fact]
    public void WrapsAtRightMarginAndScrollsIntoScrollback()
    {
        var buffer = new TerminalBuffer(4, 2);
        buffer.Feed("abcdefgh\r\nij");
        var snap = buffer.Snapshot();
        Assert.Equal("efgh", snap.LineText(0));
        Assert.Equal("ij", snap.LineText(1));
        Assert.Equal(1, snap.ScrollbackLength);
        buffer.ScrollViewport(1);
        Assert.Equal("abcd", buffer.Snapshot().LineText(0));
    }

    [Fact]
    public void CursorPositioningAndEraseLine()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("0123456789\x1b[1;4H\x1b[K");
        Assert.Equal("012", buffer.Snapshot().LineText(0));
        buffer.Feed("\x1b[2J\x1b[3;2HX");
        var snap = buffer.Snapshot();
        Assert.Equal("", snap.LineText(0));
        Assert.Equal(" X", snap.LineText(2));
    }

    [Fact]
    public void ParsesSgrColors()
    {
        var buffer = new TerminalBuffer(10, 2);
        buffer.Feed("\x1b[1;31mA\x1b[38;5;200mB\x1b[38;2;1;2;3;48;5;4mC\x1b[0mD");
        var line = buffer.Snapshot().Lines[0];
        Assert.Equal(TerminalCell.Palette(1), line[0].Foreground);
        Assert.True(line[0].Attributes.HasFlag(CellAttributes.Bold));
        Assert.Equal(TerminalCell.Palette(200), line[1].Foreground);
        Assert.Equal(TerminalCell.Rgb(1, 2, 3), line[2].Foreground);
        Assert.Equal(TerminalCell.Palette(4), line[2].Background);
        Assert.Equal(0u, line[3].Foreground);
        Assert.Equal(CellAttributes.None, line[3].Attributes);
    }

    [Fact]
    public void AlternateScreenRestoresMainScreen()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("main");
        buffer.Feed("\x1b[?1049h");
        Assert.True(buffer.IsAlternateScreen);
        buffer.Feed("alt");
        Assert.Equal("alt", buffer.Snapshot().LineText(0));
        buffer.Feed("\x1b[?1049l");
        Assert.False(buffer.IsAlternateScreen);
        var snap = buffer.Snapshot();
        Assert.Equal("main", snap.LineText(0));
        Assert.Equal(4, snap.CursorX);
    }

    [Fact]
    public void RepliesToCursorPositionReport()
    {
        var buffer = new TerminalBuffer(10, 3);
        string? reply = null;
        buffer.Response += r => reply = r;
        buffer.Feed("ab\x1b[6n");
        Assert.Equal("\x1b[1;3R", reply);
    }

    [Fact]
    public void ReadsTitleAndModes()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("\x1b]0;my title\x07\x1b[?1h\x1b[?2004h\x1b[?25l");
        Assert.Equal("my title", buffer.Title);
        Assert.True(buffer.ApplicationCursorKeys);
        Assert.True(buffer.BracketedPaste);
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void DecodesUtf8SplitAcrossChunks()
    {
        var buffer = new TerminalBuffer(10, 2);
        var bytes = System.Text.Encoding.UTF8.GetBytes("è€");
        buffer.Feed(bytes.AsSpan(0, 1));
        buffer.Feed(bytes.AsSpan(1, 3));
        buffer.Feed(bytes.AsSpan(4));
        Assert.Equal("è€", buffer.Snapshot().LineText(0));
    }

    [Fact]
    public void ScrollRegionKeepsLinesOutsideRegion()
    {
        var buffer = new TerminalBuffer(5, 4);
        buffer.Feed("top\r\n1\r\n2\r\nbot");
        buffer.Feed("\x1b[2;3r\x1b[3;1H\n");
        var snap = buffer.Snapshot();
        Assert.Equal("top", snap.LineText(0));
        Assert.Equal("2", snap.LineText(1));
        Assert.Equal("", snap.LineText(2));
        Assert.Equal("bot", snap.LineText(3));
    }

    [Fact]
    public void ResizeKeepsContent()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.Feed("a\r\nb\r\nc\r\nd");
        buffer.Resize(5, 2);
        var snap = buffer.Snapshot();
        Assert.Equal(5, snap.Columns);
        Assert.Equal("c", snap.LineText(0));
        Assert.Equal("d", snap.LineText(1));
        buffer.Resize(20, 6);
        Assert.Equal(6, buffer.Snapshot().Rows);
    }
}
