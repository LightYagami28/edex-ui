// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Files;

namespace Edex.Core.Tests;

public class FileTypesTests
{
    [Theory]
    [InlineData("photo.JPG", FileCategory.Image)]
    [InlineData("song.flac", FileCategory.Audio)]
    [InlineData("clip.mkv", FileCategory.Video)]
    [InlineData("report.pdf", FileCategory.Pdf)]
    [InlineData("Program.cs", FileCategory.Code)]
    [InlineData("settings.json", FileCategory.Data)]
    [InlineData("backup.tar.gz", FileCategory.Archive)]
    [InlineData("Makefile", FileCategory.Code)]
    [InlineData("README", FileCategory.Text)]
    [InlineData(".bashrc", FileCategory.Data)]
    [InlineData("setup.exe", FileCategory.Executable)]
    [InlineData("mystery.qqq", FileCategory.Other)]
    public void ClassifiesByExtension(string name, FileCategory expected) => Assert.Equal(expected, FileTypes.Classify(name));

    [Fact]
    public void DirectoriesAndLabels()
    {
        Assert.Equal(FileCategory.Folder, FileTypes.Classify("photos.jpg", FileEntryKind.Directory));
        Assert.Equal("PDF", FileTypes.Label("a.pdf"));
        Assert.Equal("", FileTypes.Label("archive.verylongext"));
        Assert.True(FileTypes.IsTextLike(FileCategory.Code));
        Assert.False(FileTypes.IsTextLike(FileCategory.Image));
    }
}
