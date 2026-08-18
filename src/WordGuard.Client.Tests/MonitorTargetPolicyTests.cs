using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 目标白名单判定纯函数测试：UiaWindowProbe 的快速预过滤（非目标窗口不做 UIA 遍历）依赖此判定，
/// 决定"运行中是否卡顿"。全部为纯字符串逻辑，可在无 UI 环境单测。
/// </summary>
public class MonitorTargetPolicyTests
{
    [Fact]
    public void IsMonitored_matches_exact_exe_name_case_insensitively()
    {
        Assert.True(MonitorTargetPolicy.IsMonitored("WeChat.exe", ["wechat.exe"]));
        Assert.True(MonitorTargetPolicy.IsMonitored("QQ.exe", ["qq.exe"]));
    }

    [Fact]
    public void IsMonitored_ignores_exe_suffix_on_both_sides()
    {
        // 前台进程名来自 Process.ProcessName（不带 .exe），目标配置可能带/不带 .exe，两边都应匹配
        Assert.True(MonitorTargetPolicy.IsMonitored("WeChat", ["WeChat.exe"]));
        Assert.True(MonitorTargetPolicy.IsMonitored("WeChat.exe", ["WeChat"]));
    }

    [Fact]
    public void IsMonitored_returns_false_when_not_in_targets()
    {
        Assert.False(MonitorTargetPolicy.IsMonitored("explorer.exe", ["WeChat.exe", "QQ.exe"]));
        Assert.False(MonitorTargetPolicy.IsMonitored("cmd", ["WeChat.exe"]));
    }

    [Fact]
    public void IsMonitored_returns_false_for_empty_targets()
    {
        Assert.False(MonitorTargetPolicy.IsMonitored("WeChat.exe", []));
        Assert.False(MonitorTargetPolicy.IsMonitored("WeChat.exe", null!));
    }

    [Fact]
    public void IsMonitored_is_safe_for_empty_or_whitespace_names()
    {
        Assert.False(MonitorTargetPolicy.IsMonitored("", ["WeChat.exe"]));
        Assert.False(MonitorTargetPolicy.IsMonitored("   ", ["WeChat.exe"]));
        Assert.False(MonitorTargetPolicy.IsMonitored(null!, ["WeChat.exe"]));
    }

    [Fact]
    public void IsMonitored_trims_surrounding_whitespace_in_targets()
    {
        // 用户手动配置可能带多余空格（"WeChat.exe "），不应影响匹配
        Assert.True(MonitorTargetPolicy.IsMonitored("WeChat.exe", [" WeChat.exe "]));
    }
}
