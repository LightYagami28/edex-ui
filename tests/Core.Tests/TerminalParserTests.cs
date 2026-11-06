// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Terminal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Edex.Core.Tests;

[TestClass]
public class TerminalParserTests
{
    private static TerminalBuffer Make(string input, int columns = 10, int rows = 4)
    {
        var buffer = new TerminalBuffer(columns, rows);
        buffer.Feed(input);
        return buffer;
    }

    [TestMethod]
    public void ControlCharacters()
    {
        var b = Make("ab\bc\r\nx\vy\fz\tw");
        Assert.AreEqual("ac", b.Snapshot().LineText(0));
        Assert.AreEqual("x", b.Snapshot().LineText(1));
        Assert.AreEqual("y", b.Snapshot().LineText(2).Trim());
        Assert.AreEqual("z     w", b.Snapshot().LineText(3).TrimStart());
        // backspace at column 0 does nothing; backspace right after filling the last column clears the pending wrap
        var c = Make("\b0123456789\bX");
        Assert.AreEqual("012345678X", c.Snapshot().LineText(0));
        var d = Make("0123456789\t");
        Assert.AreEqual(9, d.Snapshot().CursorX);
        d.Feed("\x7f");
        Assert.AreEqual(9, d.Snapshot().CursorX);
    }

    [TestMethod]
    public void EscapeSequences()
    {
        var b = new TerminalBuffer(10, 3);
        b.Feed("ab\x1b" + "7" + "cd\x1b" + "8" + "X");
        Assert.AreEqual("abXd", b.Snapshot().LineText(0));
        b.Feed("\x1b" + "D");
        Assert.AreEqual(1, b.Snapshot().CursorY);
        b.Feed("\x1b" + "E");
        Assert.AreEqual(2, b.Snapshot().CursorY);
        Assert.AreEqual(0, b.Snapshot().CursorX);
        b.Feed("\x1b" + "M\x1b" + "M\x1b" + "M");
        Assert.AreEqual(0, b.Snapshot().CursorY);
        b.Feed("\x1b" + "Mnew");
        Assert.AreEqual("new", b.Snapshot().LineText(0));
        Assert.AreEqual("abXd", b.Snapshot().LineText(2));
        // charset designation, intermediates and unknown escapes are swallowed
        b.Feed("\x1b(B\x1b)0\x1b*B\x1b+B\x1b#8\x1b%G\x1b Fq\x1b=Z");
        var quiet = new TerminalBuffer(30, 2);
        quiet.Feed("\x1bPignored\x1b\\ok\x1bXjunk\x1b\\more");
        Assert.AreEqual("okmore", quiet.Snapshot().Text);
        // DCS and friends are swallowed up to ST or BEL
        b.Feed("\x1b^pm\x1bq\x1b\\\x1b_apc\x1b\\");
        // full reset
        b.Feed("\x1b[?1h\x1b[?25l\x1b[?2004h\x1b[?1000h\x1b[?1006h");
        b.Feed("\x1b" + "c");
        Assert.AreEqual("", b.Snapshot().Text);
        Assert.IsFalse(b.ApplicationCursorKeys);
        Assert.IsFalse(b.BracketedPaste);
        Assert.IsFalse(b.SgrMouse);
        Assert.IsTrue(b.CursorVisible);
        Assert.AreEqual(MouseTrackingMode.None, b.MouseTracking);
    }

    [TestMethod]
    public void CsiCancelAndControlInsideSequence()
    {
        var b = new TerminalBuffer(10, 3);
        b.Feed("\x1b[12\x18" + "A\x1b[3\x1a" + "B");
        Assert.AreEqual("AB", b.Snapshot().LineText(0));
        b.Feed("\x1b[1\x1b[2;1HC");
        Assert.AreEqual("C", b.Snapshot().LineText(1));
        b.Feed("\x1b[5\nD");
        Assert.AreEqual(2, b.Snapshot().CursorY);
        Assert.AreEqual(0, b.Snapshot().CursorX);
        b.Feed("\x1b[\x01\x1b[1;1HE");
        Assert.AreEqual('E', b.Snapshot().Lines[0][0].Glyph);
    }

    [TestMethod]
    public void Osc()
    {
        var b = new TerminalBuffer(10, 3);
        b.Feed("\x1b]0;first\x07");
        Assert.AreEqual("first", b.Title);
        b.Feed("\x1b]2;second\x1b\\");
        Assert.AreEqual("second", b.Title);
        b.Feed("\x1b]1;icon\x07");
        Assert.AreEqual("second", b.Title);
        b.Feed("\x1b]nosep\x07");
        Assert.AreEqual("second", b.Title);
        b.Feed("\x1b]0;third\x1bxjunk");
        Assert.AreEqual("second", b.Title);
        b.Feed("\x1b]0;" + new string('a', 5000) + "\x07");
        Assert.AreEqual(4096 - 2, b.Title.Length);
    }

    [TestMethod]
    public void CursorMovement()
    {
        var c = new TerminalBuffer(10, 6);
        c.Feed("\x1b[4;5H\x1b[2A");
        Assert.AreEqual(1, c.Snapshot().CursorY);
        c.Feed("\x1b[E");
        Assert.AreEqual(2, c.Snapshot().CursorY);
        Assert.AreEqual(0, c.Snapshot().CursorX);
        c.Feed("\x1b[3F");
        Assert.AreEqual(0, c.Snapshot().CursorY);
        c.Feed("\x1b[7G");
        Assert.AreEqual(6, c.Snapshot().CursorX);
        c.Feed("\x1b[5`");
        Assert.AreEqual(4, c.Snapshot().CursorX);
        c.Feed("\x1b[3d");
        Assert.AreEqual(2, c.Snapshot().CursorY);
        c.Feed("\x1b[2;3f");
        Assert.AreEqual(1, c.Snapshot().CursorY);
        Assert.AreEqual(2, c.Snapshot().CursorX);
        c.Feed("\x1b[100;100H");
        Assert.AreEqual(5, c.Snapshot().CursorY);
        Assert.AreEqual(9, c.Snapshot().CursorX);
        c.Feed("\x1b[100C\x1b[100D");
        Assert.AreEqual(0, c.Snapshot().CursorX);
        c.Feed("\x1b[100B");
        Assert.AreEqual(5, c.Snapshot().CursorY);
        c.Feed("\x1b[s\x1b[1;1H\x1b[u");
        Assert.AreEqual(5, c.Snapshot().CursorY);
    }

    [TestMethod]
    public void CursorMovementStaysInScrollRegion()
    {
        var b = new TerminalBuffer(10, 6);
        b.Feed("\x1b[2;4r");
        Assert.AreEqual(0, b.Snapshot().CursorY);
        b.Feed("\x1b[3;1H\x1b[10A");
        Assert.AreEqual(1, b.Snapshot().CursorY);
        b.Feed("\x1b[10B");
        Assert.AreEqual(3, b.Snapshot().CursorY);
        b.Feed("\x1b[1;1H\x1b[10B");
        Assert.AreEqual(5, b.Snapshot().CursorY);
        b.Feed("\x1b[1;1H\x1b[10E");
        Assert.AreEqual(5, b.Snapshot().CursorY);
        b.Feed("\x1b[6;1H\x1b[10F");
        Assert.AreEqual(0, b.Snapshot().CursorY);
        // invalid region resets to the full screen
        b.Feed("\x1b[5;2r\x1b[1;7r\x1b[r");
        b.Feed("\x1b[6;1H\n");
        Assert.AreEqual(5, b.Snapshot().CursorY);
    }

    [TestMethod]
    public void ScrollRegionScrolls()
    {
        var b = new TerminalBuffer(4, 5);
        b.Feed("1\r\n2\r\n3\r\n4\r\n5");
        b.Feed("\x1b[2;4r\x1b[4;1H\n");
        Assert.AreEqual("1", b.Snapshot().LineText(0));
        Assert.AreEqual("3", b.Snapshot().LineText(1));
        Assert.AreEqual("4", b.Snapshot().LineText(2));
        Assert.AreEqual("", b.Snapshot().LineText(3));
        Assert.AreEqual("5", b.Snapshot().LineText(4));
        b.Feed("\x1b[2;1H\x1bM");
        Assert.AreEqual("", b.Snapshot().LineText(1));
        Assert.AreEqual("3", b.Snapshot().LineText(2));
        b.Feed("\x1b[S");
        Assert.AreEqual("3", b.Snapshot().LineText(1));
        b.Feed("\x1b[2T");
        Assert.AreEqual("", b.Snapshot().LineText(1));
        b.Feed("\x1b[99S\x1b[99T");
        Assert.AreEqual("", b.Snapshot().LineText(1));
    }

    [TestMethod]
    public void EraseAndEdit()
    {
        var b = new TerminalBuffer(6, 4);
        b.Feed("aaaaaa\r\nbbbbbb\r\ncccccc\r\ndddddd");
        b.Feed("\x1b[2;3H\x1b[0J");
        Assert.AreEqual("bb", b.Snapshot().LineText(1));
        Assert.AreEqual("", b.Snapshot().LineText(2));
        b.Feed("\x1b[1;1Haaaaaa\x1b[1;4H\x1b[1K");
        Assert.AreEqual("    aa", b.Snapshot().LineText(0));
        b.Feed("\x1b[2;2H\x1b[2K");
        Assert.AreEqual("", b.Snapshot().LineText(1));
        b.Feed("\x1b[1;1Hxxxxxx\x1b[2;1Hyyyyyy\x1b[2;3H\x1b[1J");
        Assert.AreEqual("", b.Snapshot().LineText(0));
        Assert.AreEqual("   yyy", b.Snapshot().LineText(1));
        b.Feed("\x1b[2J");
        Assert.AreEqual("", b.Snapshot().Text);
        b.Feed("\x1b[2J\x1b[9J\x1b[9K");
        // characters
        b.Feed("\x1b[1;1Habcdef\x1b[1;2H\x1b[2P");
        Assert.AreEqual("adef", b.Snapshot().LineText(0));
        b.Feed("\x1b[1;2H\x1b[2@");
        Assert.AreEqual("a  def", b.Snapshot().LineText(0));
        b.Feed("\x1b[1;1H\x1b[3X");
        Assert.AreEqual("   def", b.Snapshot().LineText(0));
        b.Feed("\x1b[1;5H\x1b[10X");
        Assert.AreEqual("   d", b.Snapshot().LineText(0));
        b.Feed("\x1b[1;5H\x1b[10P\x1b[1;5H\x1b[10@");
        // lines
        b.Feed("\x1b[2J\x1b[1;1H1\r\n2\r\n3\r\n4\x1b[2;1H\x1b[L");
        Assert.AreEqual("", b.Snapshot().LineText(1));
        Assert.AreEqual("2", b.Snapshot().LineText(2));
        b.Feed("\x1b[M");
        Assert.AreEqual("2", b.Snapshot().LineText(1));
        b.Feed("\x1b[9L\x1b[9M");
        b.Feed("\x1b[2;3r\x1b[1;1H\x1b[L\x1b[M\x1b[4;1H\x1b[L\x1b[M");
    }

    [TestMethod]
    public void SgrAttributes()
    {
        var b = new TerminalBuffer(40, 2);
        b.Feed("\x1b[1;2;3;4;7;8;9mA\x1b[21mB\x1b[22;23;24;27;28;29mC");
        var line = b.Snapshot().Lines[0];
        var all = CellAttributes.Bold | CellAttributes.Dim | CellAttributes.Italic | CellAttributes.Underline
            | CellAttributes.Inverse | CellAttributes.Hidden | CellAttributes.Strikethrough;
        Assert.AreEqual(all, line[0].Attributes);
        Assert.IsFalse(line[1].Attributes.HasFlag(CellAttributes.Bold));
        Assert.AreEqual(CellAttributes.None, line[2].Attributes);
        b.Feed("\x1b[m\x1b[32;43mD\x1b[39;49mE\x1b[92;104mF\x1b[0mG");
        line = b.Snapshot().Lines[0];
        Assert.AreEqual(TerminalCell.Palette(2), line[3].Foreground);
        Assert.AreEqual(TerminalCell.Palette(3), line[3].Background);
        Assert.AreEqual(0u, line[4].Foreground);
        Assert.AreEqual(0u, line[4].Background);
        Assert.AreEqual(TerminalCell.Palette(10), line[5].Foreground);
        Assert.AreEqual(TerminalCell.Palette(12), line[5].Background);
        Assert.AreEqual(0u, line[6].Background);
        // truncated extended colors consume the rest and change nothing
        b.Feed("\x1b[38;5mH\x1b[48;2;1;2mI\x1b[38;9mJ\x1b[38mK");
        line = b.Snapshot().Lines[0];
        Assert.AreEqual(0u, line[7].Foreground);
        Assert.AreEqual(0u, line[8].Background);
        b.Feed("\x1b[48;5;7mL\x1b[48;2;9;8;7mM");
        line = b.Snapshot().Lines[0];
        Assert.AreEqual(TerminalCell.Palette(7), line[11].Background);
        Assert.AreEqual(TerminalCell.Rgb(9, 8, 7), line[12].Background);
        b.Feed("\x1b[;1mN");
        Assert.IsTrue(b.Snapshot().Lines[0][13].Attributes.HasFlag(CellAttributes.Bold));
    }

    [TestMethod]
    public void PrivateModes()
    {
        var b = new TerminalBuffer(10, 3);
        b.Feed("\x1b[?1;25;2004;1006h");
        Assert.IsTrue(b.ApplicationCursorKeys);
        Assert.IsTrue(b.BracketedPaste);
        Assert.IsTrue(b.SgrMouse);
        b.Feed("\x1b[?25l");
        Assert.IsFalse(b.Snapshot().CursorVisible);
        b.Feed("\x1b[?1;25;2004;1006l");
        Assert.IsFalse(b.ApplicationCursorKeys);
        Assert.IsFalse(b.Snapshot().CursorVisible);
        b.Feed("[?25h");
        Assert.IsTrue(b.Snapshot().CursorVisible);
        foreach (var (mode, expected) in new[]
        {
            (9, MouseTrackingMode.X10), (1000, MouseTrackingMode.Normal),
            (1002, MouseTrackingMode.ButtonEvent), (1003, MouseTrackingMode.AnyEvent),
        })
        {
            b.Feed($"\x1b[?{mode}h");
            Assert.AreEqual(expected, b.MouseTracking);
            b.Feed($"\x1b[?{mode}l");
            Assert.AreEqual(MouseTrackingMode.None, b.MouseTracking);
        }
        b.Feed("\x1b[?7q\x1b[?9999h");
    }

    [TestMethod]
    public void AlternateScreenVariants()
    {
        var b = new TerminalBuffer(10, 3);
        b.Feed("main\x1b[?47h");
        Assert.IsTrue(b.IsAlternateScreen);
        b.Feed("\x1b[?47h");
        Assert.AreEqual("", b.Snapshot().LineText(0));
        b.Feed("alt\x1b[?47l");
        Assert.AreEqual("main", b.Snapshot().LineText(0));
        b.Feed("\x1b[?47l\x1b[?1047h");
        Assert.IsTrue(b.IsAlternateScreen);
        b.Feed("\x1b[?1047l");
        Assert.IsFalse(b.IsAlternateScreen);
        // scrolling in the alternate screen never reaches the scrollback
        b.Feed("\x1b[?1049h1\r\n2\r\n3\r\n4");
        Assert.AreEqual(0, b.Snapshot().ScrollbackLength);
        b.ScrollViewport(5);
        Assert.AreEqual(0, b.Snapshot().ScrollOffset);
        b.Feed("\x1b[?1049l");
    }

    [TestMethod]
    public void DeviceReports()
    {
        var b = new TerminalBuffer(10, 3);
        var replies = new List<string>();
        b.Response += replies.Add;
        b.Feed("\x1b[5n\x1b[6n\x1b[c\x1b[0c\x1b[>c\x1b[1c\x1b[7n\x1b[<c\x1b[=c");
        Assert.HasCount(1, replies);
        Assert.AreEqual("\x1b[0n\x1b[1;1R\x1b[?62;22c\x1b[?62;22c\x1b[>0;10;0c", replies[0]);
        b.Feed("x");
        Assert.HasCount(1, replies);
        // intermediate-character CSI sequences: only DECSTR (CSI ! p) resets
        b.Feed("abc\x1b[ q\x1b[\"q\x1b[$p\x1b['z\x1b[!p");
        Assert.AreEqual("", b.Snapshot().Text);
        b.Feed("\x1b[!q");
        // a buffer without a subscriber still works
        var quiet = new TerminalBuffer(10, 3);
        quiet.Feed("\x1b[6n");
    }

    [TestMethod]
    public void ResizeKeepsContent()
    {
        var b = new TerminalBuffer(6, 4);
        b.Feed("11\r\n22\r\n33\r\n44");
        b.Resize(6, 4);
        b.Resize(8, 3);
        Assert.AreEqual(8, b.Columns);
        Assert.AreEqual(3, b.Rows);
        Assert.AreEqual("22", b.Snapshot().LineText(0));
        Assert.AreEqual(1, b.Snapshot().ScrollbackLength);
        b.Resize(4, 6);
        b.Feed("\x1b[?1049h");
        b.Feed("alt");
        b.Resize(5, 3);
        Assert.IsTrue(b.IsAlternateScreen);
        Assert.AreEqual("alt", b.Snapshot().LineText(0));
        b.Feed("\x1b[?1049l");
        b.Resize(2, 2);
        b.Resize(1, 1);
        Assert.AreEqual(2, b.Columns);
        // blank rows below the cursor are dropped before history is pushed
        var c = new TerminalBuffer(6, 6);
        c.Feed("top");
        c.Resize(6, 2);
        Assert.AreEqual(0, c.Snapshot().ScrollbackLength);
        Assert.AreEqual("top", c.Snapshot().LineText(0));
        // resizing while scrolled back keeps the offset within history
        var d = new TerminalBuffer(4, 2, 3);
        d.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n6");
        d.ScrollViewport(3);
        d.Resize(4, 4);
        Assert.IsLessThanOrEqualTo(d.Snapshot().ScrollbackLength, d.Snapshot().ScrollOffset);
    }

    [TestMethod]
    public void ScrollbackLimitAndViewport()
    {
        var b = new TerminalBuffer(4, 2, 3);
        b.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n6");
        Assert.AreEqual(3, b.Snapshot().ScrollbackLength);
        b.ScrollViewport(2);
        b.Feed("\r\n7");
        Assert.AreEqual(2, b.Snapshot().ScrollOffset);
        b.ScrollViewport(-1);
        Assert.AreEqual(1, b.Snapshot().ScrollOffset);
        Assert.IsFalse(b.Snapshot().CursorVisible);
        b.ResetViewport();
        Assert.AreEqual(0, b.Snapshot().ScrollOffset);
        b.ScrollViewport(1);
        b.Feed("\x1b[3J");
        Assert.AreEqual(0, b.Snapshot().ScrollbackLength);
        Assert.AreEqual(0, b.Snapshot().ScrollOffset);
        Assert.IsNotEmpty(b.ScrollbackText());
        // growing scrollback while scrolled back keeps the view in place
        var c = new TerminalBuffer(4, 2, 10);
        c.Feed("1\r\n2\r\n3\r\n4");
        c.ScrollViewport(1);
        c.Feed("\r\n5");
        Assert.AreEqual(2, c.Snapshot().ScrollOffset);
    }

    [TestMethod]
    public void Utf8SplitAcrossFeeds()
    {
        var b = new TerminalBuffer(10, 2);
        var bytes = System.Text.Encoding.UTF8.GetBytes("è!");
        b.Feed(bytes.AsSpan(0, 1));
        b.Feed(bytes.AsSpan(1));
        Assert.AreEqual("è!", b.Snapshot().LineText(0));
        b.Feed(new string('x', 9998));
        Assert.AreEqual(10, b.Snapshot().LineText(1).Length);
        Assert.AreEqual(2, b.Snapshot().Rows);
        Assert.AreEqual(2000, b.ScrollbackLimit);
    }

    [TestMethod]
    public void WrapPendingIsClearedByCursorCommands()
    {
        var b = new TerminalBuffer(4, 2);
        b.Feed("abcd\x1b[Ax");
        Assert.AreEqual("abcx", b.Snapshot().LineText(0));
        var c = new TerminalBuffer(4, 2);
        c.Feed("abcd\x1b" + "7\x1b" + "8");
        c.Feed("e");
        Assert.AreEqual("abce", c.Snapshot().LineText(0));
        var d = new TerminalBuffer(4, 3);
        d.Feed("abcd\x1b" + "M");
        d.Feed("z");
        Assert.AreEqual("   z", d.Snapshot().LineText(0));
        Assert.AreEqual("abcd", d.Snapshot().LineText(1));
        var e = new TerminalBuffer(4, 3);
        e.Feed("abcd\r\n\r\n\r\n\r\n");
        Assert.AreEqual(2, e.Snapshot().CursorY);
    }
}
