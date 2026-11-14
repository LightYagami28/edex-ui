// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Ui;

public sealed record UiLayout
{
    public const double Margin = 12;
    public const double Gap = 10;
    public const double TopBarHeight = 46;
    public const double LeftColumnFraction = 0.30;
    public const double RightColumnFraction = 0.24;
    public const double FilesFraction = 0.55;
    public const double MinColumnWidth = 160;
    public const double MinBodyHeight = 120;
    public const double MinRightRowHeight = 40;
    public const double SideFactor = 2;
    public const int RightRowCount = 5;

    private static readonly UiPanel[] RightPanels =
    [
        UiPanel.Cpu, UiPanel.Memory, UiPanel.Network, UiPanel.Processes, UiPanel.Globe,
    ];

    private readonly Dictionary<UiPanel, UiRect> _panels;

    private UiLayout(Dictionary<UiPanel, UiRect> panels, double width, double height)
    {
        _panels = panels;
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    public IReadOnlyDictionary<UiPanel, UiRect> Panels => _panels;

    public UiRect Rect(UiPanel panel) => _panels.TryGetValue(panel, out var rect) ? rect : UiRect.Empty;

    public bool IsVisible(UiPanel panel) => !Rect(panel).IsEmpty;

    public IReadOnlyList<UiPanel> VisiblePanels
    {
        get
        {
            var visible = new List<UiPanel>();
            foreach (var panel in Enum.GetValues<UiPanel>())
            {
                if (panel != UiPanel.None && IsVisible(panel))
                {
                    visible.Add(panel);
                }
            }

            return visible;
        }
    }

    public static UiLayout Compute(double width, double height, bool showKeyboard)
    {
        var panels = new Dictionary<UiPanel, UiRect>();
        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            return new UiLayout(panels, 0, 0);
        }

        var content = new UiRect(Margin, Margin, Math.Max(0, width - Margin * SideFactor), Math.Max(0, height - Margin * SideFactor));
        if (content.IsEmpty)
        {
            return new UiLayout(panels, width, height);
        }

        panels[UiPanel.TopBar] = content.SliceTop(Math.Min(TopBarHeight, content.Height));
        var body = content.Below(panels[UiPanel.TopBar], Gap);
        if (body.Width <= 0 || body.Height < MinBodyHeight)
        {
            return new UiLayout(panels, width, height);
        }

        bool hasLeft = body.Width * LeftColumnFraction >= MinColumnWidth;
        bool hasRight = body.Width * RightColumnFraction >= MinColumnWidth && body.Height >= MinRightRowHeight * RightRowCount;
        double leftWidth = hasLeft ? body.Width * LeftColumnFraction : 0;
        double rightWidth = hasRight ? body.Width * RightColumnFraction : 0;
        double gaps = (hasLeft ? Gap : 0) + (hasRight ? Gap : 0);

        panels[UiPanel.Terminal] = new UiRect(body.X + leftWidth + (hasLeft ? Gap : 0), body.Y, Math.Max(0, body.Width - leftWidth - rightWidth - gaps), body.Height);

        if (hasLeft)
        {
            var left = new UiRect(body.X, body.Y, leftWidth, body.Height);
            panels[UiPanel.Files] = showKeyboard ? left.SliceTop(left.Height * FilesFraction) : left;
            panels[UiPanel.Keyboard] = showKeyboard ? left.Below(panels[UiPanel.Files], Gap) : UiRect.Empty;
        }

        if (hasRight)
        {
            var right = new UiRect(body.Right - rightWidth, body.Y, rightWidth, body.Height);
            for (int i = 0; i < RightRowCount; i++)
            {
                panels[RightPanels[i]] = right.Row(i, RightRowCount, Gap);
            }
        }

        return new UiLayout(panels, width, height);
    }
}
