// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Edex.Core.Input;

namespace Edex.UI.Controls;

/// <summary>On-screen keyboard. Clicking a key sends input; physical key presses light up the matching key.</summary>
public sealed class KeyboardView : Viewbox
{
    private const double Unit = 38;
    private readonly KeyboardState _state = new();
    private readonly Palette _palette;
    private readonly List<(KeyDefinition Key, Button Button)> _buttons = [];
    private readonly Func<bool> _applicationCursorKeys;

    /// <summary>Raised with the bytes a key press produces.</summary>
    public event Action<string>? Output;

    /// <summary>Raised for every key press (for click sounds).</summary>
    public event Action? KeyClicked;

    public KeyboardLayout Layout { get; }

    public KeyboardView(Palette palette, KeyboardLayout layout, Func<bool> applicationCursorKeys)
    {
        _palette = palette;
        _applicationCursorKeys = applicationCursorKeys;
        Layout = layout;
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;

        var rows = new StackPanel { Spacing = 4 };
        foreach (var row in layout.Rows)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var key in row)
            {
                var button = new Button
                {
                    Width = Unit * key.Width + 4 * (key.Width - 1),
                    Height = Unit,
                    Padding = new Thickness(0),
                    FontSize = key.Label.Length > 1 ? 11 : 15,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Focusable = false,
                };
                button.Click += (_, _) => Press(key);
                panel.Children.Add(button);
                _buttons.Add((key, button));
            }
            rows.Children.Add(panel);
        }
        Child = new Border { Padding = new Thickness(6), Child = rows };
        Refresh();
    }

    /// <summary>
    /// Types text by pressing the on-screen keys (Shift is pressed automatically). Returns false if a character has no key
    /// on this layout. Used by the smoke test to drive the real click path.
    /// </summary>
    public bool Type(string text)
    {
        foreach (var ch in text)
        {
            var c = ch.ToString();
            KeyDefinition? key = null;
            bool shift = false;
            if (ch == '\r' || ch == '\n') key = Layout.Rows.SelectMany(r => r).FirstOrDefault(k => k.Action == KeyAction.Enter);
            else if (ch == ' ') key = Layout.Rows.SelectMany(r => r).FirstOrDefault(k => k.Action == KeyAction.Space);
            else
            {
                key = Layout.Rows.SelectMany(r => r).FirstOrDefault(k => k.Action == KeyAction.Char && k.Label == c);
                if (key is null)
                {
                    key = Layout.Rows.SelectMany(r => r).FirstOrDefault(k => k.Action == KeyAction.Char &&
                        (k.ShiftLabel == c || (k.ShiftLabel is null && k.Label.ToUpperInvariant() == c && k.Label != c)));
                    shift = key is not null;
                }
            }
            if (key is null) return false;
            if (shift && !_state.Shift)
                Press(Layout.Rows.SelectMany(r => r).First(k => k.Action == KeyAction.Shift));
            Press(key);
        }
        return true;
    }

    private void Press(KeyDefinition key)
    {
        KeyClicked?.Invoke();
        var output = _state.Press(key, _applicationCursorKeys());
        if (!string.IsNullOrEmpty(output)) Output?.Invoke(output);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (key, button) in _buttons)
        {
            bool active = key.Action switch
            {
                KeyAction.Shift => _state.Shift,
                KeyAction.Control => _state.Control,
                KeyAction.Alt => _state.Alt,
                KeyAction.CapsLock => _state.CapsLock,
                _ => false,
            };
            button.Content = _state.Display(key);
            button.Foreground = active ? _palette.BackgroundBrush : _palette.ForegroundBrush;
            button.Background = active ? _palette.AccentBrush : _palette.GlowBrush;
            button.BorderBrush = _palette.BorderBrush;
        }
    }

    /// <summary>Briefly highlights the on-screen key matching a physical key press.</summary>
    public void Flash(Key key, string? text = null)
    {
        var match = _buttons.FirstOrDefault(b => Matches(b.Key, key, text));
        if (match.Button is null) return;
        match.Button.Background = _palette.AccentBrush;
        match.Button.Foreground = _palette.BackgroundBrush;
        DispatcherTimer.RunOnce(Refresh, TimeSpan.FromMilliseconds(120));
    }

    private static bool Matches(KeyDefinition def, Key key, string? text) => def.Action switch
    {
        KeyAction.Char => text is not null && (string.Equals(def.Label, text, StringComparison.OrdinalIgnoreCase) || def.ShiftLabel == text),
        KeyAction.Space => key == Key.Space || text == " ",
        KeyAction.Enter => key == Key.Enter,
        KeyAction.Backspace => key == Key.Back,
        KeyAction.Tab => key == Key.Tab,
        KeyAction.Escape => key == Key.Escape,
        KeyAction.Delete => key == Key.Delete,
        KeyAction.Shift => key is Key.LeftShift or Key.RightShift,
        KeyAction.Control => key is Key.LeftCtrl or Key.RightCtrl,
        KeyAction.Alt => key is Key.LeftAlt or Key.RightAlt,
        KeyAction.CapsLock => key == Key.CapsLock,
        KeyAction.Up => key == Key.Up,
        KeyAction.Down => key == Key.Down,
        KeyAction.Left => key == Key.Left,
        KeyAction.Right => key == Key.Right,
        KeyAction.Home => key == Key.Home,
        KeyAction.End => key == Key.End,
        _ => false,
    };
}
