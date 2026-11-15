// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Ui;

public enum UiBrush
{
    Background,
    Surface,
    Foreground,
    Muted,
    Accent,
    Danger,
    TerminalForeground,
    TerminalBackground,
    Cursor,
}

public enum UiElementKind
{
    Frame,
    Text,
    Bar,
    Polyline,
    Rule,
    Cells,
    Keys,
    Globe,
}

public enum UiTextAlign
{
    Left,
    Center,
    Right,
}

public readonly record struct UiElement
{
    public UiElement()
    {
    }

    public required UiElementKind Kind { get; init; }

    public required UiRect Bounds { get; init; }

    public UiBrush Brush { get; init; } = UiBrush.Foreground;

    public UiTextAlign Align { get; init; } = UiTextAlign.Left;

    public double Value { get; init; }

    public double Size { get; init; }

    public string Text { get; init; } = "";

    public object? Payload { get; init; }

    public UiPanel Panel { get; init; } = UiPanel.None;
}

public enum UiPanel
{
    None,
    TopBar,
    Terminal,
    Files,
    Keyboard,
    Cpu,
    Memory,
    Network,
    Processes,
    Globe,
}
