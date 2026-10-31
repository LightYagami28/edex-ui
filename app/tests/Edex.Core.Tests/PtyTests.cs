// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Pty;
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

public class PtyTests
{
    [Fact]
    public void RunsCommandInPseudoTerminal()
    {
        var options = OperatingSystem.IsWindows()
            ? new PtyOptions { Shell = "cmd.exe", Args = ["/c", "echo EDEX_PTY_OK"], Columns = 80, Rows = 10 }
            : new PtyOptions { Shell = "/bin/sh", Args = ["-c", "echo EDEX_PTY_OK"], Columns = 80, Rows = 10 };

        using var session = TerminalSession.Start(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !session.Buffer.ScrollbackText().Contains("EDEX_PTY_OK"))
            Thread.Sleep(50);

        Assert.Contains("EDEX_PTY_OK", session.Buffer.ScrollbackText());
        while (DateTime.UtcNow < deadline.AddSeconds(10) && !session.HasExited) Thread.Sleep(50);
        Assert.True(session.HasExited);
        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public void BuildsWindowsCommandLine()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("cmd.exe /c \"echo a b\" \"x\\\"y\"", WindowsPty.BuildCommandLine("cmd.exe", ["/c", "echo a b", "x\"y"]));
    }
}
