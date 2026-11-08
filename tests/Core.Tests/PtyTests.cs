// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Pty;
using Edex.Core.Terminal;

namespace Edex.Core.Tests;

[TestClass]
public class PtyTests
{
    public TestContext TestContext { get; set; }

    private const int ExitGraceSeconds = 10;
    private const int PollMs = 50;

    [TestMethod]
    public async Task RunsCommandInPseudoTerminal()
    {
        var options = OperatingSystem.IsWindows()
            ? new PtyOptions { Shell = "cmd.exe", Args = ["/c", "echo EDEX_PTY_OK"], Columns = 80, Rows = 10 }
            : new PtyOptions { Shell = "/bin/sh", Args = ["-c", "echo EDEX_PTY_OK"], Columns = 80, Rows = 10 };

        using var session = TerminalSession.Start(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !session.Buffer.ScrollbackText().Contains("EDEX_PTY_OK"))
            await Task.Delay(50, TestContext.CancellationToken);

        Assert.Contains("EDEX_PTY_OK", session.Buffer.ScrollbackText());
        var exitDeadline = deadline.AddSeconds(ExitGraceSeconds);
        while (DateTime.UtcNow < exitDeadline && !session.HasExited)
        {
            await Task.Delay(PollMs, TestContext.CancellationToken);
        }
        Assert.IsTrue(session.HasExited);
        Assert.AreEqual(0, session.ExitCode);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [SupportedOSPlatform("windows")]
    public void BuildsWindowsCommandLine()
    {
        Assert.AreEqual("cmd.exe /c \"echo a b\" \"x\\\"y\"", WindowsPty.BuildCommandLine("cmd.exe", ["/c", "echo a b", "x\"y"]));
    }
}
