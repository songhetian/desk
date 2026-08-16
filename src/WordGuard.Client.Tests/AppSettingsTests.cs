using Xunit;

namespace WordGuard.Client.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var s = new AppSettings();

        // 兜底去重窗口与日志保留默认值（PRD：默认 30s 去重、本地保留 30 天）
        Assert.Equal(30, s.CooldownSeconds);
        Assert.Equal(30, s.LogRetentionDays);

        // 词库路径默认与程序同目录
        Assert.Equal("wordlib.json", s.WordLibraryPath);
    }

    [Fact]
    public void Load_from_missing_file_returns_defaults()
    {
        var s = AppSettings.Load("nonexistent_path_that_does_not_exist.json");

        Assert.Equal(30, s.CooldownSeconds);
        Assert.Equal(30, s.LogRetentionDays);
        Assert.Equal("wordlib.json", s.WordLibraryPath);
    }

    [Fact]
    public void Save_then_Load_preserves_values()
    {
        var path = Path.Combine(Path.GetTempPath(), "wordguard_settings_roundtrip.json");
        try
        {
            var original = new AppSettings
            {
                CooldownSeconds = 45,
                LogRetentionDays = 90,
                WordLibraryPath = @"\\srv\share\wordlib.json",
            };
            original.Save(path);

            var loaded = AppSettings.Load(path);
            Assert.Equal(45, loaded.CooldownSeconds);
            Assert.Equal(90, loaded.LogRetentionDays);
            Assert.Equal(@"\\srv\share\wordlib.json", loaded.WordLibraryPath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_from_malformed_file_returns_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "wordguard_settings_broken.json");
        try
        {
            File.WriteAllText(path, "{ this is not valid json ,,,");

            var s = AppSettings.Load(path);

            Assert.Equal(30, s.CooldownSeconds);
            Assert.Equal("wordlib.json", s.WordLibraryPath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
