using System.Collections.Generic;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var s = new AppSettings();

        // 兜底去重窗口与日志保留默认值（默认 8s 去重、本地保留 30 天）
        Assert.Equal(8, s.CooldownSeconds);
        Assert.Equal(30, s.LogRetentionDays);

        // 词库路径默认与程序同目录
        Assert.Equal("wordlib.json", s.WordLibraryPath);

        // 自动删除默认关闭
        Assert.False(s.AutoDelete);
    }

    [Fact]
    public void Load_from_missing_file_returns_defaults()
    {
        var s = AppSettings.Load("nonexistent_path_that_does_not_exist.json");

        Assert.Equal(8, s.CooldownSeconds);
        Assert.Equal(30, s.LogRetentionDays);
        Assert.Equal("wordlib.json", s.WordLibraryPath);
        Assert.False(s.AutoDelete);
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

            Assert.Equal(8, s.CooldownSeconds);
            Assert.Equal("wordlib.json", s.WordLibraryPath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Deployment_config_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), "wg_deploy_roundtrip.json");
        try
        {
            var original = new AppSettings
            {
                MonitorTargets = new List<string> { "WeChat.exe", "QQ.exe" },
                AlertPopup = false,
                AlertSound = true,
                AlertHighlight = false,
                SoundFilePath = @"sounds\custom.wav",
                CooldownSeconds = 60,
                LogRetentionDays = 90,
            };
            original.Save(path);

            var loaded = AppSettings.Load(path);
            Assert.Equal(new[] { "WeChat.exe", "QQ.exe" }, loaded.MonitorTargets);
            Assert.False(loaded.AlertPopup);
            Assert.True(loaded.AlertSound);
            Assert.False(loaded.AlertHighlight);
            Assert.Equal(@"sounds\custom.wav", loaded.SoundFilePath);
            Assert.Equal(60, loaded.CooldownSeconds);
            Assert.Equal(90, loaded.LogRetentionDays);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ToMetadata_creates_deployment_config_from_settings()
    {
        var settings = new AppSettings
        {
            MonitorTargets = new List<string> { "WeChat.exe", "DingTalk.exe" },
            AlertPopup = true,
            AlertSound = false,
            AlertHighlight = true,
            SoundFilePath = "alert.wav",
            CooldownSeconds = 45,
            LogRetentionDays = 60,
        };

        var metadata = settings.ToMetadata();

        Assert.Equal(2, metadata.Targets.Count);
        Assert.Equal("WeChat.exe", metadata.Targets[0].ExeName);
        Assert.Equal("DingTalk.exe", metadata.Targets[1].ExeName);
        Assert.True(metadata.AlertPopup);
        Assert.False(metadata.AlertSound);
        Assert.True(metadata.AlertHighlight);
        Assert.Equal("alert.wav", metadata.SoundFilePath);
        Assert.Equal(45, metadata.CooldownSeconds);
        Assert.Equal(60, metadata.LogRetentionDays);
    }

    [Fact]
    public void Deployment_defaults_are_sensible()
    {
        var s = new AppSettings();
        Assert.Empty(s.MonitorTargets);
        Assert.True(s.AlertPopup);
        Assert.True(s.AlertSound);
        Assert.True(s.AlertHighlight);
        Assert.Equal("", s.SoundFilePath);
    }
}
