using System;
using System.IO;
using System.Text.Json;
using WordGuard.Client;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 监控管线测试：通过可注入的 <see cref="IWindowProbe"/> 模拟"前台窗口"，
/// 验证「被监控软件 + 含违禁词的输入文本 → 必然触发告警（orb 脉冲 + 审计落库 + AlertRaised 事件带通道）」，
/// 以及「非监控软件零打扰」「无命中词不告警」。
///
/// 这是"监控必须真的能用"的可测试保证：只要前台窗口是目标软件且输入框含违禁词，
/// 无论具体走哪个 OS 文本捕获技术，管线都应命中。
/// </summary>
public class CaptureServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wg_cap_" + Guid.NewGuid().ToString("N"));
    private readonly AuditLogStore _audit;

    public CaptureServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var libPath = Path.Combine(_dir, "wordlib.json");
        var lib = new WordLibrary
        {
            UpdatedAt = DateTime.UtcNow,
            Words =
            [
                new WordEntry { Text = "保证包过", Category = "诱导承诺", Severity = Severity.High, Enabled = true },
                new WordEntry { Text = "最低价", Category = "价格违规", Severity = Severity.High, Enabled = true },
            ],
            Metadata = new LibraryMetadata
            {
                Targets = [new TargetSpec { ExeName = "WeChat.exe" }],
                AlertPopup = true,
                AlertSound = true,
                AlertHighlight = true,
                CooldownSeconds = 30,
                LogRetentionDays = 30,
            },
        };
        File.WriteAllText(libPath, JsonSerializer.Serialize(lib, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        _libPath = libPath;
        _audit = new AuditLogStore("Data Source=" + Path.Combine(_dir, "audit.db"));
    }

    private readonly string _libPath;

    private sealed class FakeProbe : IWindowProbe
    {
        public WindowSnapshot? Next;
        public int ProbeCalls;
        public WindowSnapshot? Probe()
        {
            ProbeCalls++;
            return Next;
        }
    }

    private (CaptureService svc, FakeProbe probe, OrbStateController orb) Build()
    {
        // 需求#6：部署配置从客户端 AppSettings 提供，构造时传入 LibraryFileSource
        var config = new LibraryMetadata
        {
            Targets = [new TargetSpec { ExeName = "WeChat.exe" }],
            AlertPopup = true,
            AlertSound = true,
            AlertHighlight = true,
            CooldownSeconds = 30,
            LogRetentionDays = 30,
        };
        var lib = new LibraryFileSource(_libPath, TimeSpan.FromSeconds(30), config, watch: false);
        // 测试用较长告警窗口：避免慢环境（SQLite 写入延迟）下 3s 窗口提前过期导致的假阴性
        var orb = new OrbStateController(TimeSpan.FromHours(1));
        var dispatcher = new AlertDispatcher(lib.Metadata);
        var probe = new FakeProbe();
        var svc = new CaptureService(probe, lib, orb, dispatcher, _audit);
        return (svc, probe, orb);
    }

    [Fact]
    public void Tick_on_monitored_window_with_banned_text_fires_alert()
    {
        var (svc, probe, orb) = Build();
        var alerted = false;
        AlertEventArgs? captured = null;
        svc.AlertRaised += (_, e) => { alerted = true; captured = e; };

        probe.Next = new WindowSnapshot("WeChat.exe", @"C:\WeChat\WeChat.exe", "聊天窗口", "这笔订单保证包过", "ctx-wechat-1");
        svc.Tick();

        Assert.True(alerted, "目标软件含违禁词应触发告警");
        Assert.True(orb.CurrentState(DateTime.UtcNow) == OrbState.Alert, "orb 应进入告警态");
        Assert.Equal(1, _audit.Count);
        Assert.NotNull(captured);
        Assert.Contains(AlertChannel.Popup, captured!.Event.Channels);
        Assert.Contains(AlertChannel.Sound, captured.Event.Channels);
        Assert.Contains("保证包过", captured.Event.AlertWords);
    }

    [Fact]
    public void Tick_on_non_monitored_window_is_silent()
    {
        var (svc, probe, orb) = Build();
        var alerted = false;
        svc.AlertRaised += (_, _) => alerted = true;

        probe.Next = new WindowSnapshot("chrome.exe", @"C:\Chrome\chrome.exe", "网页", "这笔订单保证包过", "ctx-chrome-1");
        svc.Tick();

        Assert.False(alerted, "非监控软件应零打扰");
        Assert.Equal(0, _audit.Count);
        Assert.Equal(OrbState.Normal, orb.CurrentState(DateTime.UtcNow));
    }

    [Fact]
    public void Tick_on_monitored_window_with_clean_text_is_silent()
    {
        var (svc, probe, orb) = Build();
        var alerted = false;
        svc.AlertRaised += (_, _) => alerted = true;

        probe.Next = new WindowSnapshot("WeChat.exe", @"C:\WeChat\WeChat.exe", "聊天窗口", "您好，请问有什么可以帮您", "ctx-wechat-2");
        svc.Tick();

        Assert.False(alerted);
        Assert.Equal(0, _audit.Count);
    }

    [Fact]
    public void Tick_polls_the_probe_each_cycle()
    {
        var (svc, probe, _) = Build();
        probe.Next = null; // 桌面无前台窗口
        svc.Tick();
        svc.Tick();
        Assert.Equal(2, probe.ProbeCalls);
    }

    [Fact]
    public void Unchanged_text_is_not_reprocessed_tick_after_tick()
    {
        // 同一输入框文本未变：首次命中告警，其后冷却/不变不应重复产生审计行（防刷屏）
        var (svc, probe, _) = Build();
        probe.Next = new WindowSnapshot("WeChat.exe", @"C:\WeChat\WeChat.exe", "聊天窗口", "保证包过", "ctx-stable");
        svc.Tick();
        var afterFirst = _audit.Count;
        // 第二次轮询文本未变
        svc.Tick();
        Assert.Equal(afterFirst, _audit.Count);
        Assert.Equal(1, afterFirst);
    }

    [Fact]
    public void Stats_track_ticks_hits_and_alerts()
    {
        var (svc, probe, _) = Build();

        // 初始状态
        Assert.Equal(0, svc.Stats.TotalTicks);
        Assert.Equal(0, svc.Stats.TextCapturedCount);
        Assert.Equal(0, svc.Stats.AlertCount);

        // Tick 1：有违禁词 → 告警
        probe.Next = new WindowSnapshot("WeChat.exe", @"C:\WeChat\WeChat.exe", "聊天窗口", "保证包过", "ctx-1");
        svc.Tick();
        Assert.Equal(1, svc.Stats.TotalTicks);
        Assert.Equal(1, svc.Stats.TextCapturedCount);
        Assert.Equal(1, svc.Stats.AlertCount);

        // Tick 2：文本不变 → 不重复处理，捕获计数也不增加（只有文本真正变化才算一次有效捕获）
        svc.Tick();
        Assert.Equal(2, svc.Stats.TotalTicks);
        Assert.Equal(1, svc.Stats.TextCapturedCount);
        Assert.Equal(1, svc.Stats.AlertCount); // 告警不重复

        // Tick 3：无窗口 → 只累加 tick
        probe.Next = null;
        svc.Tick();
        Assert.Equal(3, svc.Stats.TotalTicks);
        Assert.Equal(1, svc.Stats.TextCapturedCount);
        Assert.Equal(1, svc.Stats.AlertCount);

        // 捕获率 = 1/3
        Assert.InRange(svc.Stats.CaptureRate, 0.33, 0.34);
    }

    public void Dispose()
    {
        _audit.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
