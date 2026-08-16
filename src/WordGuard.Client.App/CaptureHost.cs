using System.Diagnostics;
using System.IO;
using System.Linq;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 捕获宿主：把"UIA 前台窗口探测 → 监控管线（CaptureService）→ 告警弹窗/声音"串成实时循环。
///
/// <para>本类只负责 WinForms 层面的职责：500ms 定时器驱动 <see cref="CaptureService.Tick"/>、
/// 订阅 <see cref="CaptureService.AlertRaised"/> 执行弹窗与响铃、把调试日志写到本地文件。
/// 所有"监控是否命中"的核心逻辑都在 <see cref="CaptureService"/>（纯逻辑、可测试）。</para>
/// </summary>
public sealed class CaptureHost : IDisposable
{
    private readonly LibraryFileSource _lib;
    private readonly OrbStateController _orb;
    private readonly AlertDispatcher _dispatcher;
    private readonly AuditLogStore _audit;
    private readonly CaptureService _service;
    private readonly System.Windows.Forms.Timer _poll;
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
        _debugLogPath = Path.Combine(AppPaths.BaseDirectory, "wordguard.debug.log");

        // 真实前台窗口探测（UIA），通过可注入的探针喂给管线
        var probe = new UiaWindowProbe();
        _service = new CaptureService(probe, lib, orb, dispatcher, audit);
        _service.AlertRaised += OnAlert;

        lib.Reloaded += (_, status) =>
        {
            _orb.SetOnline(status.FileExists);
            DebugWrite($"[词库重载] 文件存在={status.FileExists}");
        };

        _poll = new System.Windows.Forms.Timer { Interval = 500 };
        _poll.Tick += (_, _) => _service.Tick();

        var targets = string.Join(", ", lib.Metadata.Targets.Select(t => t.ExeName));
        DebugWrite($"[CaptureHost 初始化] 监控目标: [{targets}] | 间隔=500ms");
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

    /// <summary>本地调试叠加目标（生产为空）：即时生效（经 LibraryFileSource 重建引擎）。</summary>
    public IEnumerable<string> ExtraTargetExes
    {
        set => _lib.ExtraTargetExes = value;
    }

    /// <summary>捕获管线核心（"模拟命中"共用）。</summary>
    public void Feed(string text, string targetProcess, string targetProcessPath, string contextId, string windowTitle)
        => _service.Feed(text, targetProcess, targetProcessPath, contextId, windowTitle);

    // ---- 告警表现：现代化弹窗 + 声音 ----
    private void OnAlert(object? _, AlertEventArgs e)
    {
        if (e.Event.Channels.Contains(AlertChannel.Sound))
            AlertSound.Play(ResolveSoundPath(_lib.Metadata.SoundFilePath));

        if (e.Event.Channels.Contains(AlertChannel.Popup))
        {
            // 在 UI 线程创建弹窗（定时器回调本就在 UI 线程，但稳妥起见用 BeginInvoke）
            var popup = new AlertPopupForm(
                e.Event, e.TriggeredText, e.TargetSoftware, e.WindowTitle,
                LookupCategory(e.Event.AlertWords.FirstOrDefault()));
            popup.Confirmed += () =>
            {
                foreach (var w in e.Event.ActiveWords) _lib.Acknowledge(w.Word, e.ContextId);
                _audit.UpdateDisposition(e.AuditLogId, "客服已确认");
                DebugWrite($"[告警已确认] 审计Id={e.AuditLogId}");
            };
            popup.Ignored += () =>
            {
                _audit.UpdateDisposition(e.AuditLogId, "已忽略");
                DebugWrite($"[告警已忽略] 审计Id={e.AuditLogId}");
            };
            popup.DetailsRequested += () =>
            {
                _audit.UpdateDisposition(e.AuditLogId, "已查看");
                DebugWrite($"[告警已查看] 审计Id={e.AuditLogId}");
                var viewer = new LogViewerForm(_audit);
                viewer.FormClosed += (_, _) => viewer.Dispose();
                viewer.Show();
            };
            popup.TimedOut += () =>
            {
                _audit.UpdateDisposition(e.AuditLogId, "未确认（超时）");
                DebugWrite($"[告警超时] 审计Id={e.AuditLogId}");
            };
            // WinForms 定时器回调本就在 UI 线程，直接 Show 即可
            popup.Show();
        }
    }

    /// <summary>按命中词文本从词库文件查所属分类（告警事件本身不携带分类，弹窗展示用；查询失败回退空串）。</summary>
    private string LookupCategory(string? word)
    {
        if (string.IsNullOrEmpty(word)) return "";
        try
        {
            var lib = Core.WordLibrary.LoadFromFile(_lib.FilePath);
            return lib.Words.FirstOrDefault(w =>
                string.Equals(w.Text, word, StringComparison.OrdinalIgnoreCase))?.Category ?? "";
        }
        catch { return ""; }
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

    public void Dispose()
    {
        _poll.Dispose();
        _service.Dispose();
    }

    /// <summary>
    /// 把词库 metadata 里的声音路径解析为绝对路径。相对路径相对词库文件所在目录；
    /// 未配置时回退到随包分发的默认提示音 <c>alert.wav</c>（解决"声音开关开着却无默认声音"）。
    /// 仅当默认提示音也不存在时才返回 null（此时 AlertSound 退化为系统 Beep）。
    /// </summary>
    private string? ResolveSoundPath(string? soundPath)
    {
        if (!string.IsNullOrWhiteSpace(soundPath))
        {
            if (Path.IsPathRooted(soundPath)) return soundPath;
            var libDir = Path.GetDirectoryName(_lib.FilePath) ?? AppPaths.BaseDirectory;
            return Path.Combine(libDir, soundPath!);
        }
        var bundled = Path.Combine(AppPaths.BaseDirectory, "alert.wav");
        return File.Exists(bundled) ? bundled : null;
    }
}
