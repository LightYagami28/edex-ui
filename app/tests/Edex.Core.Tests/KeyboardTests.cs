// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Input;

namespace Edex.Core.Tests;

public class KeyboardTests
{
    private static KeyDefinition Key(KeyboardLayout layout, string label) => layout.Rows.SelectMany(r => r).First(k => k.Label == label);

    [Fact]
    public void LayoutsHaveFiveRowsAndExpectedKeys()
    {
        foreach (var layout in KeyboardLayout.All)
        {
            Assert.Equal(5, layout.Rows.Count);
            Assert.Contains(layout.Rows[1], k => k.Label == "q");
        }
        Assert.Equal("é", Key(KeyboardLayout.ItIt, "è").ShiftLabel);
        Assert.Equal("{", Key(KeyboardLayout.EnUs, "[").ShiftLabel);
    }

    [Fact]
    public void ModifiersAreOneShotAndCapsToggles()
    {
        var state = new KeyboardState();
        var a = Key(KeyboardLayout.EnUs, "a");
        var one = Key(KeyboardLayout.EnUs, "1");
        var shift = Key(KeyboardLayout.EnUs, "Shift");
        var caps = Key(KeyboardLayout.EnUs, "Caps");
        Assert.Equal("a", state.Press(a));
        Assert.Null(state.Press(shift));
        Assert.Equal("!", state.Press(one));
        Assert.Equal("1", state.Press(one));
        state.Press(caps);
        Assert.Equal("A", state.Press(a));
        Assert.Equal("1", state.Press(one));
        Assert.True(state.CapsLock);
    }

    [Fact]
    public void ControlAltAndCursorKeys()
    {
        var state = new KeyboardState();
        var layout = KeyboardLayout.EnUs;
        state.Press(Key(layout, "Ctrl"));
        Assert.Equal("\x03", state.Press(Key(layout, "c")));
        state.Press(Key(layout, "Alt"));
        Assert.Equal("\u001bb", state.Press(Key(layout, "b")));
        Assert.Equal("\x1b[A", state.Press(Key(layout, "↑")));
        Assert.Equal("\x1bOA", state.Press(Key(layout, "↑"), applicationCursorKeys: true));
        state.Press(Key(layout, "Ctrl"));
        Assert.Equal("\x1b[1;5D", state.Press(Key(layout, "←")));
        Assert.Equal("\r", state.Press(Key(layout, "Enter")));
    }
}
