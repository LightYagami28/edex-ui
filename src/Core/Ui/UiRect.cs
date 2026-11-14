// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Ui;

public readonly record struct UiRect(double X, double Y, double Width, double Height)
{
    private const double InsetFactor = 2;

    public static UiRect Empty { get; } = new(0, 0, 0, 0);

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => !IsEmpty && x >= X && y >= Y && x < Right && y < Bottom;

    public UiRect Inset(double margin) => Inset(margin, margin);

    public UiRect Inset(double horizontal, double vertical) => new(
        X + horizontal,
        Y + vertical,
        Math.Max(0, Width - horizontal * InsetFactor),
        Math.Max(0, Height - vertical * InsetFactor));

    public UiRect SliceTop(double height)
    {
        double clamped = Math.Clamp(height, 0, Math.Max(0, Height));
        return new UiRect(X, Y, Width, clamped);
    }

    public UiRect Below(UiRect other, double gap) => new(
        X,
        other.Bottom + gap,
        Width,
        Math.Max(0, Height - (other.Bottom + gap - Y)));

    public UiRect Column(int index, int count, double gap)
    {
        if (count <= 0)
        {
            return Empty;
        }

        double slot = (Width + gap) / count;
        double width = Math.Max(0, slot - gap);
        return new UiRect(X + slot * Math.Clamp(index, 0, count - 1), Y, width, Height);
    }

    public UiRect Row(int index, int count, double gap)
    {
        if (count <= 0)
        {
            return Empty;
        }

        double slot = (Height + gap) / count;
        double height = Math.Max(0, slot - gap);
        return new UiRect(X, Y + slot * Math.Clamp(index, 0, count - 1), Width, height);
    }
}
