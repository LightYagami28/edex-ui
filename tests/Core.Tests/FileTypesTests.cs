// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Files;

namespace Edex.Core.Tests;

[TestClass]
public class FileTypesTests
{
    [TestMethod]
    [DataRow("photo.JPG", FileCategory.Image)]
    [DataRow("song.flac", FileCategory.Audio)]
    [DataRow("clip.mkv", FileCategory.Video)]
    [DataRow("report.pdf", FileCategory.Pdf)]
    [DataRow("Program.cs", FileCategory.Code)]
    [DataRow("settings.json", FileCategory.Data)]
    [DataRow("backup.tar.gz", FileCategory.Archive)]
    [DataRow("Makefile", FileCategory.Code)]
    [DataRow("README", FileCategory.Text)]
    [DataRow(".bashrc", FileCategory.Data)]
    [DataRow("setup.exe", FileCategory.Executable)]
    [DataRow("mystery.qqq", FileCategory.Other)]
    public void ClassifiesByExtension(string name, FileCategory expected) => Assert.AreEqual(expected, FileTypes.Classify(name));

    [TestMethod]
    public void DirectoriesAndLabels()
    {
        Assert.AreEqual(FileCategory.Folder, FileTypes.Classify("photos.jpg", FileEntryKind.Directory));
        Assert.AreEqual("PDF", FileTypes.Label("a.pdf"));
        Assert.AreEqual("", FileTypes.Label("archive.verylongext"));
        Assert.IsTrue(FileTypes.IsTextLike(FileCategory.Code));
        Assert.IsFalse(FileTypes.IsTextLike(FileCategory.Image));
    }
}
