using System;
using System.Collections.Generic;
using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>
/// 一次前台窗口探测的快照（与具体 OS 文本捕获技术解耦，便于测试与替换实现）。
/// </summary>
/// <param name="ExeName">进程 EXE 名，如 "WeChat.exe"（大小写不敏感匹配目标）。</param>
/// <param name="ExePath">进程完整路径（可选，用于更精确的目标约束）。</param>
/// <param name="WindowTitle">前台窗口标题（审计用）。</param>
/// <param name="Text">从目标输入框/可写控件读取到的当前已成型文本。</param>
/// <param name="ContextId">输入框稳定标识（同一窗口同一框不变），去重维度；变化即视为新输入。</param>
/// <param name="IsPinyin">是否为拼音输入（键盘钩子模式下为 true，需走拼音匹配）。</param>
public sealed record WindowSnapshot(
    string ExeName,
    string ExePath,
    string WindowTitle,
    string Text,
    string ContextId,
    bool IsPinyin = false);

/// <summary>前台窗口探测抽象：把"如何拿到前台窗口文本"与监控逻辑解耦。</summary>
public interface IWindowProbe
{
    /// <summary>探测当前前台（聚焦）窗口；返回 null 表示无可监控窗口或探测失败（静默跳过，不影响主流程）。</summary>
    WindowSnapshot? Probe();
}

/// <summary>一次告警触发后向 UI 层广播的参数（UI 据此弹窗/响铃，核心逻辑不依赖具体表现）。</summary>
public sealed class AlertEventArgs : EventArgs
{
    public AlertEvent Event { get; }
    public string TriggeredText { get; }
    public string TargetSoftware { get; }
    public string WindowTitle { get; }
    public string ContextId { get; }
    public long AuditLogId { get; }

    public AlertEventArgs(AlertEvent evt, string triggeredText, string targetSoftware,
        string windowTitle, string contextId, long auditLogId)
    {
        Event = evt;
        TriggeredText = triggeredText;
        TargetSoftware = targetSoftware;
        WindowTitle = windowTitle;
        ContextId = contextId;
        AuditLogId = auditLogId;
    }
}

/// <summary>捕获统计：用于监控健康度展示（轮询次数、成功捕获次数、告警次数）。</summary>
public sealed class CaptureStats
{
    public long TotalTicks { get; internal set; }
    public long TextCapturedCount { get; internal set; }
    public long AlertCount { get; internal set; }

    /// <summary>未确认的告警数（用户点击已知悉后减少）。用于悬浮球 badge 显示。</summary>
    public int UnacknowledgedAlerts { get; internal set; }

    /// <summary>最近一次捕获的文本（用于诊断面板展示，方便排查）。</summary>
    public string LastCapturedText { get; internal set; } = "";

    /// <summary>最近一次捕获的目标进程名（如 WeChat.exe）。</summary>
    public string LastTargetExe { get; internal set; } = "";

    /// <summary>最近一次捕获的窗口标题。</summary>
    public string LastWindowTitle { get; internal set; } = "";

    /// <summary>最近一次捕获的时间。</summary>
    public DateTime LastCaptureTime { get; internal set; }

    /// <summary>最近一次捕获的方式（UIA / 键盘钩子）。</summary>
    public string LastCaptureMethod { get; internal set; } = "";

    public double CaptureRate => TotalTicks == 0 ? 0.0 : (double)TextCapturedCount / TotalTicks;
}

/// <summary>
/// 监控管线核心（纯逻辑，不依赖 UIA / WinForms）：把"窗口探测 → 引擎匹配 → 告警派遣 → orb 脉冲 → 审计落库"串成一次调用。
///
/// <para>通过注入 <see cref="IWindowProbe"/> 使"正常监控"可独立测试——只要前台窗口是目标软件且输入框含违禁词，
/// 无论具体走哪种 OS 文本捕获实现，管线都应命中告警。</para>
///
/// <para>UI 表现（弹窗 / 响铃）由 <see cref="AlertRaised"/> 事件上抛，供 WinForms 层订阅执行，
/// 从而把"逻辑"与"表现"彻底分离。</para>
/// </summary>
public sealed class CaptureService : IDisposable
{
    private readonly IWindowProbe _probe;
    private readonly LibraryFileSource _lib;
    private readonly OrbStateController _orb;
    private readonly AlertDispatcher _dispatcher;
    private readonly AuditLogStore _audit;
    private readonly Dictionary<string, string> _lastText = new();

    /// <summary>捕获统计（线程安全的轻量读数，UI 展示用）。</summary>
    public CaptureStats Stats { get; } = new();

    /// <summary>告警触发时广播（携带通道/命中词/审计 Id），供 UI 弹窗与响铃。</summary>
    public event EventHandler<AlertEventArgs>? AlertRaised;

    /// <summary>命中违禁词时触发（无论是否被去重/冷却抑制），用于悬浮球闪烁等轻量提示。</summary>
    public event EventHandler? WordHit;

    public CaptureService(IWindowProbe probe, LibraryFileSource lib,
        OrbStateController orb, AlertDispatcher dispatcher, AuditLogStore audit)
    {
        _probe = probe;
        _lib = lib;
        _orb = orb;
        _dispatcher = dispatcher;
        _audit = audit;
    }

    /// <summary>一次轮询：探测前台窗口并喂入管线。探测失败/无窗口直接静默返回。</summary>
    public void Tick()
    {
        Stats.TotalTicks++;

        WindowSnapshot? snap;
        try { snap = _probe.Probe(); }
        catch { return; }
        if (snap is null) return;

        Feed(snap.Text, snap.ExeName, snap.ExePath, snap.ContextId, snap.WindowTitle, false, snap.IsPinyin);
    }

    /// <summary>捕获管线核心（轮询与"模拟命中"共用）。</summary>
    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle)
    {
        Feed(text, targetProcess, targetProcessPath, contextId, windowTitle, false, false);
    }

    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle, bool skipDedup)
    {
        Feed(text, targetProcess, targetProcessPath, contextId, windowTitle, skipDedup, false);
    }

    /// <param name="skipDedup">true 时跳过去重冷却（用于模拟测试，保证每次点击都触发告警）。</param>
    /// <param name="isPinyin">true 时走拼音匹配路径（键盘钩子模式）。</param>
    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle, bool skipDedup, bool isPinyin)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 同输入框文本未变化则不重复处理（避免每个轮询周期重复匹配/脉冲）
        if (_lastText.TryGetValue(contextId, out var prev) && prev == text)
            return;
        _lastText[contextId] = text;

        Stats.TextCapturedCount++;
        Stats.LastCapturedText = text;
        Stats.LastTargetExe = targetProcess;
        Stats.LastWindowTitle = windowTitle;
        Stats.LastCaptureTime = DateTime.UtcNow;
        Stats.LastCaptureMethod = isPinyin ? "键盘钩子(拼音)" : "UIA";

        var result = _lib.Current.ProcessCapture(
            new CaptureInput(text, targetProcess, targetProcessPath, contextId, DateTime.UtcNow, isPinyin),
            skipDedup);

        // 有命中词就触发 WordHit（用于悬浮球闪烁，不管是否被冷却抑制）
        if (result.Triggered.Count > 0)
            WordHit?.Invoke(this, EventArgs.Empty);

        var evt = _dispatcher.Dispatch(result);

        if (!evt.HasAlert) return;

        Stats.AlertCount++;
        Stats.UnacknowledgedAlerts++;
        _orb.PulseAlert(DateTime.UtcNow);

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

        AlertRaised?.Invoke(this, new AlertEventArgs(evt, text, targetProcess, windowTitle, contextId, log.Id));
    }

    /// <summary>确认一条告警（用户点击"已知悉"后调用，减少未确认计数）。</summary>
    public void AcknowledgeAlert()
    {
        if (Stats.UnacknowledgedAlerts > 0)
            Stats.UnacknowledgedAlerts--;
    }

    public void Dispose()
    {
        // 当前无可释放资源；保留以匹配 IDisposable 契约（CaptureHost 生命周期管理统一）。
    }
}
