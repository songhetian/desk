using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class MonitorEngineTests
{
    private static MonitorEngine Engine(IEnumerable<string> monitored, IEnumerable<WordEntry> words, TimeSpan cooldown)
        => new(new AhoCorasickMatcher(words), new AlertDedup(cooldown),
            monitored.Select(e => new TargetSpec { ExeName = e }));

    [Fact]
    public void Non_monitored_target_is_ignored()
    {
        var engine = Engine(new[] { "cs.exe" },
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium } },
            TimeSpan.FromSeconds(30));

        var r = engine.ProcessCapture(new CaptureInput("可以退货", "notepad.exe", "", "box1", DateTime.UtcNow));

        Assert.False(r.IsMonitoredTarget);
        Assert.Empty(r.Triggered);
    }

    [Fact]
    public void Monitored_target_with_banned_word_triggers_and_should_alert()
    {
        var engine = Engine(new[] { "cs.exe" },
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.High } },
            TimeSpan.FromSeconds(30));

        var r = engine.ProcessCapture(new CaptureInput("可以退货吗", "cs.exe", @"C:\cs\cs.exe", "box1", DateTime.UtcNow));

        Assert.True(r.IsMonitoredTarget);
        var hit = Assert.Single(r.Triggered);
        Assert.Equal("退货", hit.Word);
        Assert.Equal(Severity.High, hit.Severity);
        Assert.True(hit.ShouldAlert);
        Assert.Single(hit.Positions); // 同一词在文本中只计一次告警
    }

    [Fact]
    public void Same_word_same_context_within_cooldown_is_suppressed_on_second_capture()
    {
        var engine = Engine(new[] { "cs.exe" },
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium } },
            TimeSpan.FromSeconds(30));

        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var first = engine.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", t));
        var second = engine.ProcessCapture(new CaptureInput("还是退货", "cs.exe", "", "box1", t.AddSeconds(5)));

        Assert.True(Assert.Single(first.Triggered).ShouldAlert);
        Assert.False(Assert.Single(second.Triggered).ShouldAlert);
    }

    [Fact]
    public void Multiple_distinct_words_all_triggered()
    {
        var engine = Engine(new[] { "cs.exe" },
            new[]
            {
                new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium },
                new WordEntry { Text = "加微信", Enabled = true, Severity = Severity.High },
            },
            TimeSpan.FromSeconds(30));

        var r = engine.ProcessCapture(new CaptureInput("满意可退货也可加微信", "cs.exe", "", "box1", DateTime.UtcNow));

        Assert.Equal(2, r.Triggered.Count);
        Assert.Contains(r.Triggered, w => w.Word == "退货" && w.Severity == Severity.Medium);
        Assert.Contains(r.Triggered, w => w.Word == "加微信" && w.Severity == Severity.High);
    }

    [Fact]
    public void Disabled_word_is_ignored()
    {
        var engine = Engine(new[] { "cs.exe" },
            new[] { new WordEntry { Text = "退货", Enabled = false, Severity = Severity.Medium } },
            TimeSpan.FromSeconds(30));

        var r = engine.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));

        Assert.True(r.IsMonitoredTarget);
        Assert.Empty(r.Triggered);
    }

    [Fact]
    public void Text_change_clears_acknowledged_suppression_for_that_box()
    {
        // PRD：确认后该框不再就同词告警，直至文本变更清除该词。
        var dedup = new AlertDedup(TimeSpan.FromSeconds(30));
        var engine = new MonitorEngine(new AhoCorasickMatcher(
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium } }),
            dedup, new[] { new TargetSpec { ExeName = "cs.exe" } });

        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        engine.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", t));
        dedup.Acknowledge("退货", "box1"); // 客服已确认

        // 冷却窗口过后、且文本未变：确认态仍在 → 抑制
        var unchanged = engine.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", t.AddSeconds(31)));
        Assert.False(Assert.Single(unchanged.Triggered).ShouldAlert);

        // 文本变更：引擎清除该框确认态 → 冷却已过后可再次告警
        var changed = engine.ProcessCapture(new CaptureInput("还是退货", "cs.exe", "", "box1", t.AddSeconds(32)));
        Assert.True(Assert.Single(changed.Triggered).ShouldAlert);
    }

    [Fact]
    public void Duplicate_word_entries_take_highest_severity()
    {
        // 词库若存在重复文本的多条目，聚合时取最高严重度（而非首个命中）
        var engine = Engine(new[] { "cs.exe" },
            new[]
            {
                new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Low },
                new WordEntry { Text = "退货", Enabled = true, Severity = Severity.High },
            },
            TimeSpan.FromSeconds(30));

        var r = engine.ProcessCapture(new CaptureInput("可以退货", "cs.exe", "", "box1", DateTime.UtcNow));

        Assert.Equal(Severity.High, Assert.Single(r.Triggered).Severity);
    }

    [Fact]
    public void Exe_only_target_matches_any_path()
    {
        var engine = new MonitorEngine(new AhoCorasickMatcher(
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium } }),
            new AlertDedup(TimeSpan.FromSeconds(30)),
            new[] { new TargetSpec { ExeName = "WeChat.exe" } });

        var r = engine.ProcessCapture(new CaptureInput("可以退货", "wechat.exe", @"C:\Program Files\Tencent\WeChat\WeChat.exe", "box1", DateTime.UtcNow));
        Assert.True(r.IsMonitoredTarget);
        Assert.Single(r.Triggered);
    }

    [Fact]
    public void Exe_with_path_target_requires_path_prefix()
    {
        var engine = new MonitorEngine(new AhoCorasickMatcher(
            new[] { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.Medium } }),
            new AlertDedup(TimeSpan.FromSeconds(30)),
            new[] { new TargetSpec { ExeName = "WeChat.exe", ExePath = @"C:\Apps\WeChat\" } });

        // 路径前缀命中 → 被监控
        var ok = engine.ProcessCapture(new CaptureInput("可以退货", "wechat.exe", @"C:\Apps\WeChat\WeChat.exe", "box1", DateTime.UtcNow));
        Assert.True(ok.IsMonitoredTarget);

        // 同名 EXE 但路径不符 → 非目标（区分同名安装）
        var no = engine.ProcessCapture(new CaptureInput("可以退货", "wechat.exe", @"C:\Other\WeChat.exe", "box1", DateTime.UtcNow));
        Assert.False(no.IsMonitoredTarget);
    }
}

