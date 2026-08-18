using System;
using System.Collections.Generic;
using System.Linq;

namespace WordGuard.Client;

/// <summary>
/// 监控目标白名单判定（纯函数，无 UI / 无 OS 依赖，可单测）。
///
/// <para>用途：<see cref="App.UiaWindowProbe"/> 在每次 500ms 轮询前做<b>快速预过滤</b>——
/// 前台进程名不在目标白名单时直接跳过，避免对任意前台窗口（浏览器/资源管理器/悬浮球自身等）
/// 做昂贵的 UIA 全树遍历，从而根治"监控运行中 UI 线程被卡死、点击无响应"的问题。</para>
///
/// <para>匹配规则：大小写不敏感、自动忽略两侧的 ".exe" 后缀（<c>Process.ProcessName</c> 不带后缀、
/// 目标配置可能带/不带后缀）、忽略目标项首尾空白、空目标集合永不命中。</para>
/// </summary>
public static class MonitorTargetPolicy
{
    /// <summary>判定某前台进程名是否为监控目标。任何输入都不抛异常。</summary>
    public static bool IsMonitored(string? exeName, IEnumerable<string>? targets)
    {
        if (string.IsNullOrWhiteSpace(exeName) || targets is null) return false;
        var name = Normalize(exeName);
        if (name.Length == 0) return false;
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (string.Equals(Normalize(t), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>归一化：去首尾空白、去 ".exe" 后缀、转小写。</summary>
    public static string Normalize(string s)
        => (s ?? "").Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
}
