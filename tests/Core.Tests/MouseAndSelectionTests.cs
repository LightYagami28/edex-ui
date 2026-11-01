// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

[TestClass]
public class MouseAndSelectionTests
{
    [TestMethod]
    public void TracksMouseModesFromDecset()
    {
        var buffer = new TerminalBuffer(20, 5);
        Assert.AreEqual(MouseTrackingMode.None, buffer.MouseTracking);
        buffer.Feed("\x1b[?1002h\x1b[?1006h");
        Assert.AreEqual(MouseTrackingMode.ButtonEvent, buffer.MouseTracking);
        Assert.IsTrue(buffer.SgrMouse);
        buffer.Feed("\x1b[?1002l\x1b[?1006l");
        Assert.AreEqual(MouseTrackingMode.None, buffer.MouseTracking);
        Assert.IsFalse(buffer.SgrMouse);
    }

    [TestMethod]
    public void EncodesSgrMouseEvents()
    {
        Assert.AreEqual("\x1b[<0;3;2M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.Left, 2, 1));
        Assert.AreEqual("\x1b[<0;3;2m", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Release, MouseButton.Left, 2, 1));
        Assert.AreEqual("\x1b[<18;1;1M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.Right, 0, 0, MouseModifiers.Control));
        Assert.AreEqual("\x1b[<64;5;5M", MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Press, MouseButton.WheelUp, 4, 4));
        Assert.AreEqual("\x1b[<32;2;2M", MouseEncoder.Encode(MouseTrackingMode.ButtonEvent, true, MouseEventKind.Motion, MouseButton.Left, 1, 1));
    }

    [TestMethod]
    public void EncodesLegacyMouseEventsAndRespectsMode()
    {
        Assert.AreEqual("\x1b[M !!", MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Press, MouseButton.Left, 0, 0));
        Assert.AreEqual("\x1b[M#!!", MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Release, MouseButton.Left, 0, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.X10, false, MouseEventKind.Release, MouseButton.Left, 0, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Motion, MouseButton.Left, 0, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.ButtonEvent, true, MouseEventKind.Motion, MouseButton.None, 0, 0));
        Assert.IsNotNull(MouseEncoder.Encode(MouseTrackingMode.AnyEvent, true, MouseEventKind.Motion, MouseButton.None, 0, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.None, true, MouseEventKind.Press, MouseButton.Left, 0, 0));
    }

    [TestMethod]
    public void SelectionExtractsTextAcrossLines()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.Feed("hello you\r\nsecond\r\nthird");
        var snap = buffer.Snapshot();
        var sel = new TextSelection(new CellPosition(3, 1), new CellPosition(6, 0));
        Assert.AreEqual("you\nseco", sel.GetText(snap));
        Assert.IsTrue(sel.Contains(9, 0));
        Assert.IsFalse(sel.Contains(5, 0));
    }

    [TestMethod]
    public void WordAndLineSelection()
    {
        var buffer = new TerminalBuffer(30, 2);
        buffer.Feed("ls /usr/local/bin  -la");
        var snap = buffer.Snapshot();
        Assert.AreEqual("/usr/local/bin", TextSelection.Word(snap, new CellPosition(8, 0)).GetText(snap));
        Assert.AreEqual("ls", TextSelection.Word(snap, new CellPosition(0, 0)).GetText(snap));
        Assert.AreEqual("ls /usr/local/bin  -la", TextSelection.Line(snap, 0).GetText(snap));
    }
}
