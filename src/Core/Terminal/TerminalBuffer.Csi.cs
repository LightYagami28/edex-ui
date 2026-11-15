// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
{
    private int[] ParseParams()
    {
        var raw = StripPrefix(_params.ToString());
        return raw.Length == 0 ? [] : raw.Split(ParamSeparators).Select(ParseParam).ToArray();
    }

    private string StripPrefix(string raw)
    {
        if (raw.Length == 0 || raw[0] is not ('?' or '>' or '<' or '='))
        {
            return raw;
        }

        _csiPrefix = raw[0];
        return raw[1..];
    }

    private static int ParseParam(string part)
    {
        var digits = new string(part.TakeWhile(char.IsAsciiDigit).Take(MaxParamDigits).ToArray());
        return digits.Length == 0 ? 0 : int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static readonly char[] ParamSeparators = [';', ':'];

    private static int Arg(int[] p, int index, int fallback = 1) => index < p.Length && p[index] > 0 ? p[index] : fallback;

    private void Csi(char final)
    {
        var p = ParseParams();
        if (HasIntermediate(final) || HasPrefix(final, p))
        {
            return;
        }

        CsiCommand(final, p);
        if (final is not 'm' and not 'n' and not 'c')
        {
            _wrapPending = false;
        }
    }

    private bool HasIntermediate(char final)
    {
        if (_params.Length == 0 || !IntermediateChars.Contains(_params[^1], StringComparison.Ordinal))
        {
            return false;
        }

        if (final == 'p' && _params.ToString() == "!")
        {
            FullReset();
        }

        return true;
    }

    private bool HasPrefix(char final, int[] p)
    {
        if (_csiPrefix == '?')
        {
            PrivateMode(final, p);
            return true;
        }

        if (_csiPrefix is not ('>' or '<' or '='))
        {
            return false;
        }

        if (final == 'c' && _csiPrefix == '>')
        {
            _responses.Append("\x1b[>0;10;0c");
        }

        return true;
    }

    private static readonly Dictionary<char, Action<TerminalBuffer, int[]>> CsiActions = BuildCsiActions();

    private static Dictionary<char, Action<TerminalBuffer, int[]>> BuildCsiActions()
    {
        var actions = new Dictionary<char, Action<TerminalBuffer, int[]>>
        {
            ['A'] = (b, p) => b.CursorUp(p),
            ['B'] = (b, p) => b.CursorDown(p),
            ['e'] = (b, p) => b.CursorDown(p),
            ['C'] = (b, p) => b.CursorForward(p),
            ['a'] = (b, p) => b.CursorForward(p),
            ['D'] = (b, p) => b.CursorBack(p),
            ['E'] = (b, p) => b.NextLine(p),
            ['F'] = (b, p) => b.PreviousLine(p),
            ['G'] = (b, p) => b.CursorColumn(p),
            ['`'] = (b, p) => b.CursorColumn(p),
            ['d'] = (b, p) => b.CursorRow(p),
        };
        AddEditActions(actions);
        return actions;
    }

    private static void AddEditActions(Dictionary<char, Action<TerminalBuffer, int[]>> actions)
    {
        actions['H'] = (b, p) => b.CursorPosition(p);
        actions['f'] = (b, p) => b.CursorPosition(p);
        actions['J'] = (b, p) => b.EraseDisplay(Arg(p, 0, 0));
        actions['K'] = (b, p) => b.EraseLine(Arg(p, 0, 0));
        actions['L'] = (b, p) => b.InsertLines(Arg(p, 0));
        actions['M'] = (b, p) => b.DeleteLines(Arg(p, 0));
        actions['P'] = (b, p) => b.DeleteChars(Arg(p, 0));
        actions['@'] = (b, p) => b.InsertChars(Arg(p, 0));
        actions['X'] = (b, p) => b.EraseChars(Arg(p, 0));
        actions['S'] = (b, p) => b.ScrollUp(Arg(p, 0));
        actions['T'] = (b, p) => b.ScrollDown(Arg(p, 0));
        actions['m'] = (b, p) => b.Sgr(p);
        actions['r'] = (b, p) => b.SetScrollRegion(p);
        actions['s'] = (b, _) => b.SaveCursor();
        actions['u'] = (b, _) => b.RestoreCursor();
        actions['n'] = (b, p) => b.DeviceStatus(Arg(p, 0, 0));
        actions['c'] = (b, p) => b.DeviceAttributes(p);
    }

    private void CsiCommand(char final, int[] p)
    {
        if (CsiActions.TryGetValue(final, out var action))
        {
            action(this, p);
        }
    }
}
