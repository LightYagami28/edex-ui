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

        double leftWidth = ColumnWidth(body.Width * LeftColumnFraction, true);
        double rightWidth = ColumnWidth(body.Width * RightColumnFraction, body.Height >= MinRightRowHeight * RightRowCount);
        double leftSpan = SpanWithGap(leftWidth);
        double rightSpan = SpanWithGap(rightWidth);

        panels[UiPanel.Terminal] = new UiRect(body.X + leftSpan, body.Y, Math.Max(0, body.Width - leftSpan - rightSpan), body.Height);
        PlaceLeftColumn(panels, new UiRect(body.X, body.Y, leftWidth, body.Height), showKeyboard);
        PlaceRightColumn(panels, new UiRect(body.Right - rightWidth, body.Y, rightWidth, body.Height));
        return new UiLayout(panels, width, height);
    }

    private static double ColumnWidth(double width, bool fits) => fits && width >= MinColumnWidth ? width : 0;

    private static double SpanWithGap(double columnWidth) => columnWidth > 0 ? columnWidth + Gap : 0;

    private static void PlaceLeftColumn(Dictionary<UiPanel, UiRect> panels, UiRect column, bool showKeyboard)
    {
        if (column.IsEmpty)
        {
            return;
        }

        if (!showKeyboard)
        {
            panels[UiPanel.Files] = column;
            return;
        }

        panels[UiPanel.Files] = column.SliceTop(column.Height * FilesFraction);
        panels[UiPanel.Keyboard] = column.Below(panels[UiPanel.Files], Gap);
    }

    private static void PlaceRightColumn(Dictionary<UiPanel, UiRect> panels, UiRect column)
    {
        if (column.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < RightRowCount; i++)
        {
            panels[RightPanels[i]] = column.Row(i, RightRowCount, Gap);
        }
    }
}
