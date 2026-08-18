using System.Collections.Generic;
using System.Linq;
using WordGuard.Client;
using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 监控目标"可靠选择"的纯逻辑测试：把 UI 勾选（来自软件目录）+ 手动补充 合并为最终目标列表，
/// 自动补 .exe 后缀、大小写不敏感去重、过滤空白（需求#4 解决手动输入易错）。
/// </summary>
public class MonitoredTargetTests
{
    [Fact]
    public void Build_merges_catalog_checked_and_manual_and_dedupes()
    {
        var b = new MonitoredTargetBuilder();
        var result = b.Build(new[] { "WeChat.exe", "QQ.exe" }, new[] { "QQ.exe", "WXWork.exe" });

        Assert.Equal(new[] { "WeChat.exe", "QQ.exe", "WXWork.exe" }, result);
    }

    [Fact]
    public void Build_appends_exe_suffix_when_missing()
    {
        var b = new MonitoredTargetBuilder();
        var result = b.Build(new[] { "WeChat" }, new[] { "notepad" });

        Assert.Equal(new[] { "WeChat.exe", "notepad.exe" }, result);
    }

    [Fact]
    public void Build_ignores_blank_and_whitespace()
    {
        var b = new MonitoredTargetBuilder();
        var result = b.Build(new[] { "  ", "" }, new[] { "  QQ  " });

        Assert.Equal(new[] { "QQ.exe" }, result);
    }

    [Fact]
    public void Build_dedupes_case_insensitively()
    {
        var b = new MonitoredTargetBuilder();
        var result = b.Build(new[] { "WeChat.exe" }, new[] { "wechat.EXE" });

        Assert.Single(result);
        Assert.Equal("WeChat.exe", result[0]);
    }

    [Fact]
    public void ProcessCatalog_returns_distinct_exe_names()
    {
        var list = new ProcessCatalog().ListRunningExes();

        Assert.NotNull(list);
        Assert.NotEmpty(list);
        // 去重（大小写不敏感）
        Assert.Equal(list.Distinct(System.StringComparer.OrdinalIgnoreCase).Count(), list.Count);
        // 每个条目都是非空、以 .exe 结尾、且不含路径分隔符（避免把路径当 EXE 名）
        Assert.All(list, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.EndsWith(".exe", s, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\\', s);
            Assert.DoesNotContain('/', s);
        });
    }
}
