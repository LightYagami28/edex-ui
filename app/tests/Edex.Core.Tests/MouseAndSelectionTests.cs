// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

public class MouseAndSelectionTests
{
    [Fact]
    public void TracksMouseModesFromDecset()
    {
        var buffer = new TerminalBuffer(20, 5);
        Assert.Equal(MouseTrackingMode.None, buffer.MouseTracking);
        buffer.Feed("\x1b[?1002h\x1b[?1006h");
        Assert.Equal(MouseTrackingMode.ButtonEvent, buffer.MouseTracking);
        Assert.True(buffer.SgrMouse);
        buffer.Feed("\x1b[?1002l\x1b[?1006l");
        Assert.Equal(MouseTrackingMode.None, buffer.MouseTracking);
        Assert.False(buffer.SgrMouse);
    }

    [Fact]
    public void EncodesSgrMouseEvents()
    {
        Assert.Equal("\x1b[<0;3;2M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.Left, 2, 1));
        Assert.Equal("\x1b[<0;3;2m", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Release, MouseButton.Left, 2, 1));
        Assert.Equal("\x1b[<18;1;1M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.Right, 0, 0, MouseModifiers.Control));
        Assert.Equal("\x1b[<64;5;5M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.WheelUp, 4, 4));
        Assert.Equal("\x1b[<32;2;2M", MouseEncoder.Encode(MouseTrackingMode.ButtonEvent, true, MouseEventKind.Motion, MouseButton.Left, 1, 1));
    }

    [Fact]
    public void EncodesLegacyMouseEventsAndRespectsMode()
    {
        Assert.Equal("\x1b[M !!", MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Press, MouseButton.Left, 0, 0));
        Assert.Equal("\x1b[M#!!", MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Release, MouseButton.Left, 0, 0));
        Assert.Null(MouseEncoder.Encode(MouseTrackingMode.X10, false, MouseEventKind.Release, MouseButton.Left, 0, 0));
        Assert.Null(MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Motion, MouseButton.Left, 0, 0));
        Assert.Null(MouseEncoder.Encode(MouseTrackingMode.ButtonEvent, true, MouseEventKind.Motion, MouseButton.None, 0, 0));
        Assert.NotNull(MouseEncoder.Encode(MouseTrackingMode.AnyEvent, true, MouseEventKind.Motion, MouseButton.None, 0, 0));
        Assert.Null(MouseEncoder.Encode(MouseTrackingMode.None, true, MouseEventKind.Press, MouseButton.Left, 0, 0));
    }

    [Fact]
    public void SelectionExtractsTextAcrossLines()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("hello you\r\nsecond\r\nthird");
        var snap = buffer.Snapshot();
        var sel = new TextSelection(new CellPosition(3, 1), new CellPosition(6, 0));
        Assert.Equal("you\nseco", sel.GetText(snap));
        Assert.True(sel.Contains(9, 0));
        Assert.False(sel.Contains(5, 0));
    }

    [Fact]
    public void WordAndLineSelection()
    {
        var buffer = new TerminalBuffer(30, 2);
        buffer.Feed("ls /usr/local/bin  -la");
        var snap = buffer.Snapshot();
        Assert.Equal("/usr/local/bin", TextSelection.Word(snap, new CellPosition(8, 0)).GetText(snap));
        Assert.Equal("ls", TextSelection.Word(snap, new CellPosition(0, 0)).GetText(snap));
        Assert.Equal("ls /usr/local/bin  -la", TextSelection.Line(snap, 0).GetText(snap));
    }
}
