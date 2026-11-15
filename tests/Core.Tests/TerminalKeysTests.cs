// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Input;

namespace Edex.Core.Tests;

[TestClass]
public sealed class TerminalKeysTests
{
    [TestMethod]
    public void CursorKeysUseCsiOrApplicationMode()
    {
        Assert.AreEqual("\x1b[A", TerminalKeys.Cursor(TerminalCursorKey.Up, false));
        Assert.AreEqual("\x1b[B", TerminalKeys.Cursor(TerminalCursorKey.Down, false));
        Assert.AreEqual("\x1b[C", TerminalKeys.Cursor(TerminalCursorKey.Right, false));
        Assert.AreEqual("\x1b[D", TerminalKeys.Cursor(TerminalCursorKey.Left, false));
        Assert.AreEqual("\x1bOA", TerminalKeys.Cursor(TerminalCursorKey.Up, true));
        Assert.AreEqual("\x1bOF", TerminalKeys.Cursor(TerminalCursorKey.End, true));
    }

    [TestMethod]
    public void EditingKeysUseTildeSequences()
    {
        Assert.AreEqual("\x1b[H", TerminalKeys.Cursor(TerminalCursorKey.Home, false));
        Assert.AreEqual("\x1b[F", TerminalKeys.Cursor(TerminalCursorKey.End, false));
        Assert.AreEqual("\x1b[5~", TerminalKeys.Cursor(TerminalCursorKey.PageUp, false));
        Assert.AreEqual("\x1b[6~", TerminalKeys.Cursor(TerminalCursorKey.PageDown, false));
        Assert.AreEqual("\x1b[3~", TerminalKeys.Cursor(TerminalCursorKey.Delete, false));
        Assert.AreEqual("\x1b[2~", TerminalKeys.Cursor(TerminalCursorKey.Insert, false));
        Assert.AreEqual(string.Empty, TerminalKeys.Cursor((TerminalCursorKey)999, false));
    }

    [TestMethod]
    public void FunctionKeysUseSs3ForTheFirstFourAndTildeAfterwards()
    {
        Assert.AreEqual("\x1bOP", TerminalKeys.Function(1));
        Assert.AreEqual("\x1bOQ", TerminalKeys.Function(2));
        Assert.AreEqual("\x1bOR", TerminalKeys.Function(3));
        Assert.AreEqual("\x1bOS", TerminalKeys.Function(4));
        Assert.AreEqual("\x1b[15~", TerminalKeys.Function(5));
        Assert.AreEqual("\x1b[24~", TerminalKeys.Function(12));
    }

    [TestMethod]
    public void FunctionKeysOutsideTheRangeAreEmpty()
    {
        Assert.AreEqual(string.Empty, TerminalKeys.Function(0));
        Assert.AreEqual(string.Empty, TerminalKeys.Function(13));
        Assert.AreEqual(string.Empty, TerminalKeys.Function(-1));
    }

    [TestMethod]
    public void ControlMapsLettersToControlCodes()
    {
        Assert.AreEqual("\x01", TerminalKeys.Control('a'));
        Assert.AreEqual("\x01", TerminalKeys.Control('A'));
        Assert.AreEqual("\x00", TerminalKeys.Control('@'));
        Assert.AreEqual("\x1f", TerminalKeys.Control('_'));
        Assert.AreEqual("\x1a", TerminalKeys.Control('Z'));
    }

    [TestMethod]
    public void ControlMapsQuestionMarkToDelete()
    {
        Assert.AreEqual("\x7f", TerminalKeys.Control('?'));
    }

    [TestMethod]
    public void ControlIgnoresOtherCharacters()
    {
        Assert.IsNull(TerminalKeys.Control(' '));
        Assert.IsNull(TerminalKeys.Control('é'));
        Assert.IsNull(TerminalKeys.Control('1'));
    }

    [TestMethod]
    public void EditingKeysRespectModifiers()
    {
        Assert.AreEqual("\b", TerminalKeys.Backspace(false));
        Assert.AreEqual("\x1b\b", TerminalKeys.Backspace(true));
        Assert.AreEqual("\t", TerminalKeys.Tab(false));
        Assert.AreEqual("\x1b[Z", TerminalKeys.Tab(true));
        Assert.AreEqual("\r", TerminalKeys.Return(false));
        Assert.AreEqual("\x1b\r", TerminalKeys.Return(true));
    }

    [TestMethod]
    public void EscapeWithPrefixesTheSequence()
    {
        Assert.AreEqual("\x1b[1;5C", TerminalKeys.EscapeWith("[1;5C"));
        Assert.AreEqual("\x1b", TerminalKeys.EscapeWith(string.Empty));
    }
}
