// SPDX-License-Identifier: GPL-3.0-only

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
{
    private const int MaxOscLength = 4096;
    private const int MaxParamDigits = 9;
    private const char Bell = '\a';
    private const char Cancel = '\x18';
    private const char Substitute = '\x1A';
    private const char Escape = '\x1B';
    private const char Delete = '\x7F';
    private const char CsiFinalFirst = '@';
    private const char CsiFinalLast = '~';
    private const char IntermediateFirst = '0';
    private const char IntermediateLast = '~';
    private const string IntermediateChars = " !\"$'";

    private static readonly Dictionary<State, Action<TerminalBuffer, char>> StateActions = new()
    {
        [State.Ground] = (b, c) => b.ProcessGround(c),
        [State.Escape] = (b, c) => b.EscapeChar(c),
        [State.EscapeIntermediate] = (b, c) => b.ProcessEscapeIntermediate(c),
        [State.Charset] = (b, _) => b._state = State.Ground,
        [State.Csi] = (b, c) => b.ProcessCsi(c),
        [State.Osc] = (b, c) => b.ProcessOsc(c),
        [State.OscEscape] = (b, c) => b.ProcessOscEscape(c),
        [State.Dcs] = (b, c) => b.ProcessDcs(c),
        [State.DcsEscape] = (b, c) => b.ProcessDcsEscape(c),
    };

    private void Process(char c)
    {
        if (StateActions.TryGetValue(_state, out var action))
        {
            action(this, c);
        }
    }

    private void ProcessEscapeIntermediate(char c)
    {
        if (c >= IntermediateFirst && c <= IntermediateLast)
        {
            _state = State.Ground;
        }
    }

    private void ProcessOscEscape(char c)
    {
        if (c == '\\')
        {
            Osc();
        }

        _state = State.Ground;
    }

    private void ProcessDcsEscape(char c)
    {
        _state = c == '\\' ? State.Ground : State.Dcs;
    }

    private void ProcessGround(char c)
    {
        if (c < ' ' || c == Delete)
        {
            Control(c);
        }
        else
        {
            Print(c);
        }
    }

    private void ProcessCsi(char c)
    {
        if (c >= CsiFinalFirst && c <= CsiFinalLast)
        {
            FinishCsi(c);
        }
        else if (c >= ' ')
        {
            _params.Append(c);
        }
        else
        {
            CsiControl(c);
        }
    }

    private void FinishCsi(char c)
    {
        Csi(c);
        _state = State.Ground;
    }

    private void CsiControl(char c)
    {
        if (c == Escape)
        {
            _state = State.Escape;
        }
        else if (c is Cancel or Substitute)
        {
            _state = State.Ground;
        }
        else
        {
            Control(c);
        }
    }

    private void ProcessOsc(char c)
    {
        if (c == Bell)
        {
            Osc();
            _state = State.Ground;
            return;
        }

        if (c == Escape)
        {
            _state = State.OscEscape;
            return;
        }

        if (_osc.Length < MaxOscLength)
        {
            _osc.Append(c);
        }
    }

    private void ProcessDcs(char c)
    {
        if (c == Escape)
        {
            _state = State.DcsEscape;
            return;
        }

        if (c == Bell)
        {
            _state = State.Ground;
        }
    }

    private static readonly Dictionary<char, Action<TerminalBuffer>> EscapeActions = BuildEscapeActions();

    private static Dictionary<char, Action<TerminalBuffer>> BuildEscapeActions()
    {
        var actions = new Dictionary<char, Action<TerminalBuffer>>
        {
            ['['] = b => b.BeginCsi(),
            [']'] = b => b.BeginOsc(),
            ['7'] = b => b.SaveCursor(),
            ['8'] = b => b.RestoreCursor(),
            ['D'] = b => b.LineFeed(),
            ['E'] = b => b.NextLineFeed(),
            ['M'] = b => b.ReverseIndex(),
            ['c'] = b => b.FullReset(),
        };
        AddStateChanges(actions, "PX^_", State.Dcs);
        AddStateChanges(actions, "()*+", State.Charset);
        AddStateChanges(actions, "#% ", State.EscapeIntermediate);
        return actions;
    }

    private static void AddStateChanges(Dictionary<char, Action<TerminalBuffer>> actions, string keys, State state)
    {
        foreach (char key in keys)
        {
            actions[key] = b => b._state = state;
        }
    }

    private void BeginCsi()
    {
        _params.Clear();
        _csiPrefix = '\0';
        _state = State.Csi;
    }

    private void BeginOsc()
    {
        _osc.Clear();
        _state = State.Osc;
    }

    private void NextLineFeed()
    {
        _cursorX = 0;
        LineFeed();
    }

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        if (EscapeActions.TryGetValue(c, out var action))
        {
            action(this);
        }
    }

    private static readonly Dictionary<char, Action<TerminalBuffer>> ControlActions = new()
    {
        [Escape] = b => b._state = State.Escape,
        ['\r'] = b => b.CarriageReturn(),
        ['\n'] = b => b.LineFeed(),
        ['\v'] = b => b.LineFeed(),
        ['\f'] = b => b.LineFeed(),
        ['\b'] = b => b.Backspace(),
        ['\t'] = b => b.Tab(),
    };

    private void Control(char c)
    {
        if (ControlActions.TryGetValue(c, out var action))
        {
            action(this);
        }
    }

    private void Print(char c)
    {
        if (_wrapPending)
        {
            _cursorX = 0;
            LineFeed();
            _wrapPending = false;
        }
        _screen[_cursorY][_cursorX] = new TerminalCell(c, _fg, _bg, _attrs);
        if (_cursorX == Columns - 1)
        {
            _wrapPending = true;
        }
        else
        {
            _cursorX++;
        }
    }

    private void Osc()
    {
        var text = _osc.ToString();
        int sep = text.IndexOf(';');
        if (sep <= 0)
        {
            return;
        }

        var kind = text[..sep];
        if (kind is "0" or "2")
        {
            Title = text[(sep + 1)..];
        }
    }
}
