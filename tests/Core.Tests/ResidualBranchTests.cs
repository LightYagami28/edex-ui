// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Input;
using Edex.Core.Monitoring;

namespace Edex.Core.Tests;

[TestClass]
public class ResidualBranchTests
{
    [TestMethod]
    public void PressingNoKeyOutputsNothing()
    {
        Assert.IsNull(new KeyboardState().Press(null!));
    }

    [TestMethod]
    [DataRow("3", "Desktop")]
    [DataRow("4", "Desktop")]
    [DataRow("5", "Desktop")]
    [DataRow("6", "Desktop")]
    [DataRow("7", "Desktop")]
    [DataRow("15", "Desktop")]
    [DataRow("16", "Desktop")]
    [DataRow("8", "Laptop")]
    [DataRow("9", "Laptop")]
    [DataRow("10", "Laptop")]
    [DataRow("14", "Laptop")]
    [DataRow("11", "Tablet")]
    [DataRow("30", "Tablet")]
    [DataRow("31", "Tablet")]
    [DataRow("32", "Tablet")]
    [DataRow("17", "Server")]
    [DataRow("23", "Server")]
    [DataRow("28", "Server")]
    [DataRow("13", "All in One")]
    [DataRow("", "")]
    [DataRow("2", "Other")]
    public void ChassisCodesMapToNames(string code, string expected)
    {
        Assert.AreEqual(expected, HardwareInfo.ChassisName(code));
    }
}
