using MiHouse.Core.Settings;

namespace MiHouse.Core.Tests.Settings;

public sealed class AppearanceStoreTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "MiHouse.Core.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewSettingsUseDocumentedDefaults()
    {
        var settings = new AppearanceSettings();

        Assert.Equal(AppThemeMode.System, settings.ThemeMode);
        Assert.Equal(AccentColorSource.System, settings.AccentSource);
        Assert.Equal("#0078D4", settings.CustomAccentColor);
        Assert.True(settings.UseColoredIcons);
        Assert.True(settings.AnimationsEnabled);
    }

    [Fact]
    public void RoundTripPreservesAllValues()
    {
        var store = new AppearanceStore(_tempDirectory);
        var settings = new AppearanceSettings
        {
            ThemeMode = AppThemeMode.Dark,
            AccentSource = AccentColorSource.Custom,
            CustomAccentColor = "#123456",
            UseColoredIcons = false,
            AnimationsEnabled = false,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(AppThemeMode.Dark, loaded.ThemeMode);
        Assert.Equal(AccentColorSource.Custom, loaded.AccentSource);
        Assert.Equal("#123456", loaded.CustomAccentColor);
        Assert.False(loaded.UseColoredIcons);
        Assert.False(loaded.AnimationsEnabled);
    }

    [Fact]
    public void MissingFileLoadsDefaults()
    {
        var store = new AppearanceStore(_tempDirectory);

        var loaded = store.Load();

        Assert.Equal(AppThemeMode.System, loaded.ThemeMode);
        Assert.Equal(AccentColorSource.System, loaded.AccentSource);
        Assert.Equal("#0078D4", loaded.CustomAccentColor);
        Assert.True(loaded.UseColoredIcons);
        Assert.True(loaded.AnimationsEnabled);
    }

    [Fact]
    public void InvalidJsonLoadsDefaults()
    {
        var store = new AppearanceStore(_tempDirectory);
        WriteJson("not json at all");

        var loaded = store.Load();

        Assert.Equal(AppThemeMode.System, loaded.ThemeMode);
        Assert.Equal("#0078D4", loaded.CustomAccentColor);
        Assert.True(loaded.UseColoredIcons);
    }

    [Fact]
    public void UnknownEnumStringFallsBackToDefault()
    {
        var store = new AppearanceStore(_tempDirectory);
        WriteJson(
            """{"Version":1,"ThemeMode":"Neon","AccentSource":"System","CustomAccentColor":"#123456","UseColoredIcons":true,"AnimationsEnabled":true}""");

        var loaded = store.Load();

        Assert.Equal(AppThemeMode.System, loaded.ThemeMode);
        Assert.Equal("#123456", loaded.CustomAccentColor);
    }

    [Fact]
    public void UnknownAccentSourceFallsBackToDefault()
    {
        var store = new AppearanceStore(_tempDirectory);
        WriteJson(
            """{"Version":1,"ThemeMode":"Dark","AccentSource":"Rainbow","CustomAccentColor":"#123456","UseColoredIcons":true,"AnimationsEnabled":true}""");

        var loaded = store.Load();

        Assert.Equal(AppThemeMode.Dark, loaded.ThemeMode);
        Assert.Equal(AccentColorSource.System, loaded.AccentSource);
    }

    [Fact]
    public void LowercaseColorIsNormalizedToUppercase()
    {
        var settings = new AppearanceSettings { CustomAccentColor = "#abcdef" };

        Assert.Equal("#ABCDEF", settings.CustomAccentColor);
    }

    [Fact]
    public void InvalidColorFallsBackToDefault()
    {
        var settings = new AppearanceSettings { CustomAccentColor = "red" };

        Assert.Equal("#0078D4", settings.CustomAccentColor);
    }

    [Fact]
    public void UnchangedPropertyDoesNotRaiseChanged()
    {
        var settings = new AppearanceSettings();
        var changed = 0;
        settings.Changed += (_, _) => changed++;

        settings.ThemeMode = AppThemeMode.System;
        settings.CustomAccentColor = "#0078D4";

        Assert.Equal(0, changed);
    }

    [Fact]
    public void ChangedPropertyRaisesChanged()
    {
        var settings = new AppearanceSettings();
        var changed = 0;
        settings.Changed += (_, _) => changed++;

        settings.ThemeMode = AppThemeMode.Dark;

        Assert.Equal(1, changed);
    }

    [Fact]
    public void SaveUsesAtomicReplacementWithoutTempResidue()
    {
        var store = new AppearanceStore(_tempDirectory);
        var settings = new AppearanceSettings();

        store.Save(settings);
        store.Save(settings);

        var files = Directory.GetFiles(_tempDirectory);
        Assert.Single(files);
        Assert.EndsWith("appearance.json", files[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private void WriteJson(string json)
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "appearance.json"), json);
    }
}
