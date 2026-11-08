// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Input;

namespace Edex.Core.Tests;

[TestClass]
public class KeyboardTests
{
    private static KeyDefinition Key(KeyboardLayout layout, string label) => layout.Rows.SelectMany(r => r).First(k => k.Label == label);

    [TestMethod]
    public void LayoutsHaveFiveRowsAndExpectedKeys()
    {
        foreach (var layout in KeyboardLayout.All)
        {
            Assert.HasCount(5, layout.Rows);
            Assert.Contains(k => k.Label == "q", layout.Rows[1]);
        }
        Assert.AreEqual("é", Key(KeyboardLayout.ItIt, "è").ShiftLabel);
        Assert.AreEqual("{", Key(KeyboardLayout.EnUs, "[").ShiftLabel);
    }

    [TestMethod]
    public void ModifiersAreOneShotAndCapsToggles()
    {
        var state = new KeyboardState();
        var a = Key(KeyboardLayout.EnUs, "a");
        var one = Key(KeyboardLayout.EnUs, "1");
        var shift = Key(KeyboardLayout.EnUs, "Shift");
        var caps = Key(KeyboardLayout.EnUs, "Caps");
        Assert.AreEqual("a", state.Press(a));
        Assert.IsNull(state.Press(shift));
        Assert.AreEqual("!", state.Press(one));
        Assert.AreEqual("1", state.Press(one));
        state.Press(caps);
        Assert.AreEqual("A", state.Press(a));
        Assert.AreEqual("1", state.Press(one));
        Assert.IsTrue(state.CapsLock);
    }

    [TestMethod]
    public void ControlAltAndCursorKeys()
    {
        var state = new KeyboardState();
        var layout = KeyboardLayout.EnUs;
        state.Press(Key(layout, "Ctrl"));
        Assert.AreEqual("\x03", state.Press(Key(layout, "c")));
        state.Press(Key(layout, "Alt"));
        Assert.AreEqual("\u001bb", state.Press(Key(layout, "b")));
        Assert.AreEqual("\x1b[A", state.Press(Key(layout, "↑")));
        Assert.AreEqual("\x1bOA", state.Press(Key(layout, "↑"), applicationCursorKeys: true));
        state.Press(Key(layout, "Ctrl"));
        Assert.AreEqual("\x1b[1;5D", state.Press(Key(layout, "←")));
        Assert.AreEqual("\r", state.Press(Key(layout, "Enter")));
    }
}
