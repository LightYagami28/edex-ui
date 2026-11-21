// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using Edex.Core.Settings;

namespace Edex.Core.Tests;

[TestClass]
public sealed class SettingsValidationTests
{
    private string _directory = "";

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "edex-settings-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void DefaultSettingsAreValid()
    {
        var problems = new AppSettings().Validate();
        Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
    }

    [TestMethod]
    public void ValidateDetectsAllOutOfRangeValues()
    {
        var settings = new AppSettings
        {
            Shell = " ",
            Theme = "",
            FontSize = AppSettings.MaxFontSize + 1,
            Scrollback = 0,
            MonitorIntervalMs = AppSettings.MinMonitorIntervalMs - 1,
            SoundVolume = 1.5,
            StartDirectory = "relativo",
        };

        var problems = settings.Validate();
        Assert.AreEqual(7, problems.Count, string.Join("; ", problems));
    }

    [TestMethod]
    public void ValidateRejectsNonFiniteNumbers()
    {
        var settings = new AppSettings { FontSize = double.NaN, SoundVolume = double.PositiveInfinity };
        var problems = settings.Validate();
        Assert.AreEqual(2, problems.Count, string.Join("; ", problems));
    }

    [TestMethod]
    public void LoadStrictThrowsOnInvalidStoredValues()
    {
        var store = new SettingsStore(_directory);
        var invalid = new AppSettings { FontSize = 3, SoundVolume = -0.1 };
        File.WriteAllText(store.FilePath, JsonSerializer.Serialize(invalid));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => store.LoadStrict());
        StringAssert.Contains(exception.Message, "FontSize");
        StringAssert.Contains(exception.Message, "SoundVolume");
    }

    [TestMethod]
    public void LoadStrictWrapsCorruptedJson()
    {
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, "{ json non valido");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => store.LoadStrict());
        Assert.IsNotNull(exception.InnerException);
        Assert.IsInstanceOfType<JsonException>(exception.InnerException);
    }

    [TestMethod]
    public void LoadFallsBackToDefaultsOnCorruptedJson()
    {
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, "{ json non valido");

        var settings = store.Load();
        Assert.AreEqual(new AppSettings().FontSize, settings.FontSize);
    }

    [TestMethod]
    public void SaveValidatedDoesNotWriteInvalidSettings()
    {
        var store = new SettingsStore(_directory);
        var invalid = new AppSettings { Scrollback = -5 };

        Assert.ThrowsExactly<InvalidOperationException>(() => store.SaveValidated(invalid));
        Assert.IsFalse(File.Exists(store.FilePath));
    }

    [TestMethod]
    public void SaveValidatedWritesSettingsReadableByLoadStrict()
    {
        var store = new SettingsStore(_directory);
        var valid = new AppSettings { Theme = "matrix", FontSize = 18, Scrollback = 500 };

        store.SaveValidated(valid);
        var loaded = store.LoadStrict();
        Assert.AreEqual(valid.Theme, loaded.Theme);
        Assert.AreEqual(valid.FontSize, loaded.FontSize);
        Assert.AreEqual(valid.Scrollback, loaded.Scrollback);
    }
}
