using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 捕获宿主：把"UIA 轮询捕获 → 监控引擎 → 告警派遣 → 悬浮球/声音/弹窗/审计"串成实时管线。
///
/// UIA 每 500ms 读取前台聚焦可写控件的文本（中文可靠来源），仅在文本变化时喂给引擎；
/// 命中后按通道开关触发对应表现，并写入审计日志（确认/超时回写 disposition）。
///
/// 内置调试日志（wordguard.debug.log）便于排查真机问题。
/// </summary>
public sealed class CaptureHost : IDisposable
{
    private readonly LibraryFileSource _lib;
    private readonly OrbStateController _orb;
    private readonly AlertDispatcher _dispatcher;
    private readonly AuditLogStore _audit;
    private readonly System.Windows.Forms.Timer _poll;
    private readonly Dictionary<string, string> _lastText = new();
    private readonly string _debugLogPath;

    // 状态回调（让 Program.cs / 托盘能展示当前状态）
    public Action<string>? OnStatusUpdate { get; set; }

    public CaptureHost(LibraryFileSource lib, OrbStateController orb,
        AlertDispatcher dispatcher, AuditLogStore audit)
    {
        _lib = lib;
        _orb = orb;
        _dispatcher = dispatcher;
        _audit = audit;
        _debugLogPath = Path.Combine(AppContext.BaseDirectory, "wordguard.debug.log");

        _lib.Reloaded += (_, status) =>
        {
            _orb.SetOnline(status.FileExists);
            DebugWrite($"[词库重载] 文件存在={status.FileExists}");
        };

        _poll = new System.Windows.Forms.Timer { Interval = 500 };
        _poll.Tick += (_, _) => PollForeground();

        var targets = string.Join(", ", lib.Metadata.Targets.Select(t => t.ExeName));
        DebugWrite($"[CaptureHost 初始化] 监控目标: [{targets}] | 间隔=500ms | 去重={lib.Metadata.CooldownSeconds}s");
    }

    public void Start()
    {
        _poll.Start();
        DebugWrite("[CaptureHost 已启动]");
        var targets = string.Join(", ", _lib.Metadata.Targets.Select(t => t.ExeName));
        OnStatusUpdate?.Invoke($"监控中 | 目标: [{targets}]");
    }

    public void Stop()
    {
        _poll.Stop();
        DebugWrite("[CaptureHost 已停止]");
    }

    // ---- UIA 轮询 ----
    private void PollForeground()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null) return;

            var (exe, path) = ExeOf(el);
            if (string.IsNullOrEmpty(exe)) return;

            // 关键：大小写不敏感匹配（Windows 进程名不区分大小写），支持可选路径约束
            var isTarget = IsTarget(exe, path);
            if (!isTarget) return;  // 非目标进程，零打扰

            var text = ReadText(el);
            if (text is null || text.Length == 0) return;

            var windowHandle = el.Current.NativeWindowHandle.ToString();
            var key = exe + "|" + windowHandle;
            if (_lastText.TryGetValue(key, out var prev) && prev == text) return; // 未变化不重复处理
            _lastText[key] = text;

            var title = WindowTitleOf(el);
            DebugWrite($"[捕获] 进程={exe} | 路径={path} | 标题={title} | 文本长度={text.Length} | 前50字=\"{SafeTruncate(text, 50)}\"");

            Feed(text, exe, path, key, title);
        }
        catch (Exception ex)
        {
            DebugWrite($"[PollForeground 异常] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool IsTarget(string exe, string path)
    {
        foreach (var t in _lib.Metadata.Targets)
        {
            if (!string.Equals(t.ExeName, exe, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(t.ExePath))
                return true;
            if (!string.IsNullOrWhiteSpace(path) &&
                path.Replace('/', '\\').StartsWith(
                    t.ExePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (string Exe, string Path) ExeOf(AutomationElement el)
    {
        try
        {
            var pid = (int)el.GetCurrentPropertyValue(AutomationElement.ProcessIdProperty);
            var proc = Process.GetProcessById(pid);
            return (proc.ProcessName + ".exe", proc.MainModule?.FileName ?? "");
        }
        catch { return ("", ""); }
    }

    /// <summary>读取 UIA 元素文本，按优先级尝试多种 Pattern。</summary>
    private static string? ReadText(AutomationElement el)
    {
        // 1. TextPattern（富文本框，如聊天输入框）
        try
        {
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var p) && p is TextPattern tp)
                return tp.DocumentRange.GetText(-1);
        }
        catch { }

        // 2. ValuePattern（普通输入框）
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern vpat)
                return vpat.Current.Value;
        }
        catch { }

        // 3. Name 属性兜底（某些控件只暴露 Name）
        try
        {
            var name = el.Current.Name;
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }

        return null;
    }

    private static string WindowTitleOf(AutomationElement el)
    {
        var e = el;
        while (e is not null && e.Current.ControlType != ControlType.Window)
            e = TreeWalker.ControlViewWalker.GetParent(e);
        return e?.Current.Name ?? "";
    }

    /// <summary>捕获管线核心（UIA 轮询与「模拟命中」共用）。</summary>
    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle)
    {
        var result = _lib.Current.ProcessCapture(new CaptureInput(text, targetProcess, targetProcessPath, contextId, DateTime.UtcNow));
        var evt = _dispatcher.Dispatch(result);

        // 无论是否告警都记录命中信息到调试日志
        var anyAlerting = result.Triggered.Any(w => w.ShouldAlert);
        if (anyAlerting)
        {
            DebugWrite($"[引擎结果] ShouldAlert=True | 触发词={string.Join(",", result.Triggered.Where(w => w.ShouldAlert).Select(w => w.Word))}" +
                       $" | 通道=[{string.Join(",", evt.Channels)}]" +
                       $" | Severity={evt.TopSeverity}");
        }
        else if (result.Triggered.Count > 0)
        {
            DebugWrite($"[引擎结果] ShouldAlert=False(被抑制/冷却) | 匹配词={string.Join(",", result.Triggered.Select(w => w.Word))}");
        }

        if (!evt.HasAlert) return;

        _orb.PulseAlert(DateTime.UtcNow);
        if (evt.Channels.Contains(AlertChannel.Sound))
            AlertSound.Play(ResolveSoundPath(_lib.Metadata.SoundFilePath));

        var log = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            TargetSoftware = targetProcess,
            WindowTitle = windowTitle,
            TriggeredContent = text,
            MatchedWords = evt.ActiveWords.Select(w => new MatchedWord(w.Id.ToString(), w.Word)).ToList(),
            Severity = evt.TopSeverity,
            Disposition = "已记日志",
            AlertChannels = string.Join(",", evt.Channels.Select(c => c.ToString().ToLowerInvariant())),
        };
        _audit.Add(log);
        DebugWrite($"[审计已写入] Id={log.Id}");

        if (evt.Channels.Contains(AlertChannel.Popup))
        {
            var popup = new AlertPopupForm(evt, text, targetProcess, windowTitle);
            popup.Confirmed += () =>
            {
                foreach (var w in evt.ActiveWords) _lib.Acknowledge(w.Word, contextId);
                _audit.UpdateDisposition(log.Id, "客服已确认");
                DebugWrite($"[告警已确认] 审计Id={log.Id}");
            };
            popup.TimedOut += () =>
            {
                _audit.UpdateDisposition(log.Id, "未确认（超时）");
                DebugWrite($"[告警超时] 审计Id={log.Id}");
            };
            popup.Show();
        }
    }

    // ---- 调试日志 ----
    private void DebugWrite(string line)
    {
        try
        {
            File.AppendAllText(_debugLogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
        }
        catch { /* 日志写入失败不应影响主流程 */ }
    }

    public void Dispose() => _poll.Dispose();

    // ---- 工具方法 ----
    private static string SafeTruncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    /// <summary>
    /// 把词库 metadata 里的声音路径解析为绝对路径。相对路径相对词库文件所在目录；
    /// 空路径表示使用系统默认提示音（返回 null）。
    /// </summary>
    private string? ResolveSoundPath(string? soundPath)
    {
        if (string.IsNullOrWhiteSpace(soundPath)) return null;
        if (Path.IsPathRooted(soundPath)) return soundPath;
        var libDir = Path.GetDirectoryName(_lib.FilePath) ?? AppContext.BaseDirectory;
        return Path.Combine(libDir, soundPath!);
    }
}
