using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WordGuard.Client;

/// <summary>提供"当前可监控的软件清单"的数据源抽象，便于测试替换（需求#4 可靠选择）。</summary>
public interface IAppCatalog
{
    /// <summary>返回当前正在运行的进程 EXE 名（统一带 .exe 后缀、去重、按名称排序）。</summary>
    IReadOnlyList<string> ListRunningExes();
}

/// <summary>基于 Windows 进程列表的软件目录（真实实现）。忽略无权限访问的进程。</summary>
public sealed class ProcessCatalog : IAppCatalog
{
    public IReadOnlyList<string> ListRunningExes()
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var exe = name.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                seen.Add(exe);
            }
            catch
            {
                // 系统/受保护进程无权限访问，忽略即可
            }
        }
        return seen.OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>
/// 把用户在 UI 上"勾选的运行中软件"与"手动补充项"合并为最终监控目标列表。
/// 解决手动输入易错（需求#4）：自动补 .exe 后缀、大小写不敏感去重、过滤空白。
/// </summary>
public sealed class MonitoredTargetBuilder
{
    /// <summary>合并勾选项与手动项，产出干净的目标列表（去重、补后缀、过滤空白）。</summary>
    public IReadOnlyList<string> Build(IEnumerable<string>? checkedFromCatalog, IEnumerable<string>? manualEntries)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in (checkedFromCatalog ?? Enumerable.Empty<string>())
                     .Concat(manualEntries ?? Enumerable.Empty<string>()))
        {
            var name = Normalize(raw);
            if (name is null) continue;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw!.Trim();
        if (!s.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
            s += ".exe";
        return s;
    }
}

/// <summary>一个预设常用软件的标识：显示名 + EXE 名。</summary>
public sealed record PresetApp(string DisplayName, string ExeName);

/// <summary>合并后的软件选择项：包含是否为预设、是否正在运行的状态。</summary>
public sealed record AppSelectionItem(string DisplayName, string ExeName, bool IsPreset, bool IsRunning);

/// <summary>
/// 预设常用客服软件目录（需求#4：可靠选择替代手动输入）。
/// 提供常见即时通讯/客服软件列表，配合 <see cref="ProcessCatalog"/> 的运行中进程列表，
/// 合并展示为统一的勾选界面，降低手动输入进程名的出错风险。
/// </summary>
public sealed class PresetAppCatalog
{
    /// <summary>预设常用软件列表（中文客服场景常见 IM/聊天工具）。</summary>
    public static IReadOnlyList<PresetApp> Presets { get; } = new List<PresetApp>
    {
        new("微信", "WeChat.exe"),
        new("QQ", "QQ.exe"),
        new("企业微信", "WXWork.exe"),
        new("钉钉", "DingTalk.exe"),
        new("千牛", "AliWorkbench.exe"),
        new("飞书", "Feishu.exe"),
        new("Chrome 浏览器", "chrome.exe"),
        new("Edge 浏览器", "msedge.exe"),
        new("旺旺", "WangWang.exe"),
        new("京麦", "jm.exe"),
    };

    /// <summary>
    /// 把预设软件列表与当前运行中进程列表合并为统一的选择项列表。
    /// 预设软件标记 IsPreset=true，运行中进程标记 IsRunning=true；
    /// 非预设的运行中进程也列出（DisplayName = ExeName），方便用户勾选。
    /// 大小写不敏感去重。
    /// </summary>
    public static IReadOnlyList<AppSelectionItem> MergeWithRunning(
        IReadOnlyList<PresetApp> presets,
        IReadOnlyList<string> runningExes)
    {
        var result = new List<AppSelectionItem>();
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // 先放预设软件
        foreach (var p in presets)
        {
            if (string.IsNullOrWhiteSpace(p.ExeName)) continue;
            var exeName = NormalizeExe(p.ExeName);
            if (exeName is null) continue;
            if (!seen.Add(exeName)) continue;

            var isRunning = runningExes.Any(r => r.Equals(exeName, System.StringComparison.OrdinalIgnoreCase));
            result.Add(new AppSelectionItem(p.DisplayName, exeName, true, isRunning));
        }

        // 再放运行中但不在预设列表的进程
        foreach (var exe in runningExes)
        {
            var exeName = NormalizeExe(exe);
            if (exeName is null) continue;
            if (!seen.Add(exeName)) continue;

            result.Add(new AppSelectionItem(exeName, exeName, false, true));
        }

        return result;
    }

    private static string? NormalizeExe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw!.Trim();
        if (!s.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
            s += ".exe";
        return s;
    }
}
