using System;
using System.Collections.Generic;
using WordGuard.Client;
using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 预设常用客服软件目录 + 运行中进程合并测试（需求#4：可靠选择替代手动输入）。
/// </summary>
public class PresetAppCatalogTests
{
    [Fact]
    public void Presets_include_common_im_software()
    {
        var presets = PresetAppCatalog.Presets;

        Assert.NotEmpty(presets);
        // 必须包含主流即时通讯工具
        Assert.Contains(presets, p => p.ExeName.Equals("WeChat.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(presets, p => p.ExeName.Equals("QQ.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(presets, p => p.ExeName.Equals("WXWork.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(presets, p => p.ExeName.Equals("DingTalk.exe", StringComparison.OrdinalIgnoreCase));
        // 每个预设都有显示名和 EXE 名
        Assert.All(presets, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(p.ExeName));
            Assert.EndsWith(".exe", p.ExeName, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void MergeWithRunning_marks_presets_as_running()
    {
        var presets = new List<PresetApp>
        {
            new("微信", "WeChat.exe"),
            new("QQ", "QQ.exe"),
            new("钉钉", "DingTalk.exe"),
        };
        var running = new List<string> { "WeChat.exe", "chrome.exe" };

        var merged = PresetAppCatalog.MergeWithRunning(presets, running);

        // 微信：预设 + 运行中
        var wechat = Assert.Single(merged, m => m.ExeName.Equals("WeChat.exe", StringComparison.OrdinalIgnoreCase));
        Assert.True(wechat.IsPreset);
        Assert.True(wechat.IsRunning);
        Assert.Equal("微信", wechat.DisplayName);

        // QQ：预设但未运行
        var qq = Assert.Single(merged, m => m.ExeName.Equals("QQ.exe", StringComparison.OrdinalIgnoreCase));
        Assert.True(qq.IsPreset);
        Assert.False(qq.IsRunning);

        // chrome.exe：运行中但非预设
        var chrome = Assert.Single(merged, m => m.ExeName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase));
        Assert.False(chrome.IsPreset);
        Assert.True(chrome.IsRunning);
        Assert.Equal("chrome.exe", chrome.DisplayName); // 非预设用 EXE 名作显示名
    }

    [Fact]
    public void MergeWithRunning_dedupes_case_insensitively()
    {
        var presets = new List<PresetApp>
        {
            new("微信", "WeChat.exe"),
        };
        var running = new List<string> { "wechat.EXE" };

        var merged = PresetAppCatalog.MergeWithRunning(presets, running);

        Assert.Single(merged);
        Assert.True(merged[0].IsRunning);
        Assert.True(merged[0].IsPreset);
    }

    [Fact]
    public void MergeWithRunning_handles_empty_inputs()
    {
        var merged = PresetAppCatalog.MergeWithRunning(new List<PresetApp>(), new List<string>());
        Assert.Empty(merged);
    }
}
