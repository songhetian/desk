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
public sealed record WindowSnapshot(
    string ExeName,
    string ExePath,
    string WindowTitle,
    string Text,
    string ContextId);

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

    /// <summary>告警触发时广播（携带通道/命中词/审计 Id），供 UI 弹窗与响铃。</summary>
    public event EventHandler<AlertEventArgs>? AlertRaised;

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
        WindowSnapshot? snap;
        try { snap = _probe.Probe(); }
        catch { return; } // 探针异常不应拖垮监控循环
        if (snap is null) return;

        Feed(snap.Text, snap.ExeName, snap.ExePath, snap.ContextId, snap.WindowTitle);
    }

    /// <summary>捕获管线核心（轮询与"模拟命中"共用）。</summary>
    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 同输入框文本未变化则不重复处理（避免每个轮询周期重复匹配/脉冲）
        if (_lastText.TryGetValue(contextId, out var prev) && prev == text)
            return;
        _lastText[contextId] = text;

        var result = _lib.Current.ProcessCapture(new CaptureInput(text, targetProcess, targetProcessPath, contextId, DateTime.UtcNow));
        var evt = _dispatcher.Dispatch(result);

        if (!evt.HasAlert) return;

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

    public void Dispose()
    {
        // 当前无可释放资源；保留以匹配 IDisposable 契约（CaptureHost 生命周期管理统一）。
    }
}
