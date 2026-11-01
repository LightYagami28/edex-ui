// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Terminal;

/// <summary>A cell position in a snapshot: zero-based column and viewport row.</summary>
public readonly record struct CellPosition(int Column, int Row) : IComparable<CellPosition>
{
    public int CompareTo(CellPosition other) => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);
}

/// <summary>A linear (stream) text selection between two cells, inclusive.</summary>
public readonly record struct TextSelection(CellPosition Anchor, CellPosition Active)
{
    public CellPosition Start => Anchor.CompareTo(Active) <= 0 ? Anchor : Active;
    public CellPosition End => Anchor.CompareTo(Active) <= 0 ? Active : Anchor;
    public bool IsEmpty => Anchor == Active;

    public bool Contains(int column, int row)
    {
        var p = new CellPosition(column, row);
        return p.CompareTo(Start) >= 0 && p.CompareTo(End) <= 0;
    }

    /// <summary>Text covered by the selection. Trailing blanks of each line are dropped and lines are joined with '\n'.</summary>
    public string GetText(TerminalSnapshot snapshot)
    {
        var start = Start;
        var end = End;
        var sb = new StringBuilder();
        for (int row = Math.Max(0, start.Row); row <= Math.Min(snapshot.Rows - 1, end.Row); row++)
        {
            var line = snapshot.Lines[row];
            int from = row == start.Row ? Math.Clamp(start.Column, 0, line.Length) : 0;
            int to = row == end.Row ? Math.Clamp(end.Column, -1, line.Length - 1) : line.Length - 1;
            var part = new StringBuilder();
            for (int c = from; c <= to; c++) part.Append(line[c].Char == '\0' ? ' ' : line[c].Char);
            if (row > start.Row) sb.Append('\n');
            sb.Append(part.ToString().TrimEnd());
        }
        return sb.ToString();
    }

    /// <summary>Selects the word (letters, digits and path characters) around a cell.</summary>
    public static TextSelection Word(TerminalSnapshot snapshot, CellPosition at)
    {
        if (at.Row < 0 || at.Row >= snapshot.Rows) return new TextSelection(at, at);
        var line = snapshot.Lines[at.Row];
        int col = Math.Clamp(at.Column, 0, line.Length - 1);
        Func<char, bool> same = IsWordChar(line[col].Char) ? IsWordChar : IsSpace(line[col].Char) ? IsSpace : _ => false;
        int from = col, to = col;
        while (from > 0 && same(line[from - 1].Char)) from--;
        while (to < line.Length - 1 && same(line[to + 1].Char)) to++;
        return new TextSelection(new CellPosition(from, at.Row), new CellPosition(to, at.Row));
    }

    /// <summary>Selects a whole line.</summary>
    public static TextSelection Line(TerminalSnapshot snapshot, int row) =>
        new(new CellPosition(0, row), new CellPosition(snapshot.Columns - 1, row));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || "-_./~:@%+#=?&\\".Contains(c);

    private static bool IsSpace(char c) => c == '\0' || char.IsWhiteSpace(c);
}
