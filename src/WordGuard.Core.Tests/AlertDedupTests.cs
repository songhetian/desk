using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class AlertDedupTests
{
    [Fact]
    public void First_occurrence_alerts()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        Assert.True(d.ShouldAlert("退货", "box1", new DateTime(2026, 1, 1, 10, 0, 0)));
    }

    [Fact]
    public void Repeat_within_cooldown_is_suppressed()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        Assert.False(d.ShouldAlert("退货", "box1", t.AddSeconds(5)));
    }

    [Fact]
    public void After_cooldown_alerts_again()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        Assert.True(d.ShouldAlert("退货", "box1", t.AddSeconds(31)));
    }

    [Fact]
    public void Acknowledged_suppresses_even_after_cooldown()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        d.Acknowledge("退货", "box1");
        Assert.False(d.ShouldAlert("退货", "box1", t.AddSeconds(31)));
    }

    [Fact]
    public void Same_word_different_context_shares_cooldown()
    {
        // PRD：跨输入框的同词也应遵循去重窗口，避免刷屏。box1 命中后，
        // 30s 内 box2 输入同词应被抑制（而非立即再告警）。
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        Assert.False(d.ShouldAlert("退货", "box2", t.AddSeconds(5))); // 跨框仍被冷却窗口抑制
    }

    [Fact]
    public void Same_word_different_context_alerts_after_cooldown_expires()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        Assert.True(d.ShouldAlert("退货", "box2", t.AddSeconds(31))); // 冷却过后允许再告警
    }

    [Fact]
    public void Reset_context_clears_acknowledgement_for_that_box()
    {
        // 文本变更 / 新会话：清除某框的"已确认"抑制，但跨框冷却仍生效
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        d.Acknowledge("退货", "box1");
        d.Reset("box1");
        Assert.True(d.ShouldAlert("退货", "box1", t.AddSeconds(31))); // 确认已清除且冷却已过
    }

    [Fact]
    public async System.Threading.Tasks.Task Thread_safety_holds_under_parallel_load()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var exceptions = 0;
        var tasks = new System.Threading.Tasks.Task[8];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 500; j++)
                    {
                        d.ShouldAlert("退货", "box" + (j % 4), t);
                        d.Acknowledge("退货", "box" + (j % 4));
                        if (j % 7 == 0) d.Reset("box" + (j % 4));
                    }
                }
                catch (Exception) { System.Threading.Interlocked.Increment(ref exceptions); }
            });
        }
        await System.Threading.Tasks.Task.WhenAll(tasks);
        Assert.Equal(0, exceptions); // 并发下无异常 / 无损坏
    }

    [Fact]
    public void Reset_clears_acknowledgement()
    {
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);
        d.ShouldAlert("退货", "box1", t);
        d.Acknowledge("退货", "box1");
        d.Reset("退货", "box1");
        // Reset 清除"已确认"抑制；越过共享冷却窗口后该词可再次告警（冷却仍在窗口内则继续抑制）
        Assert.True(d.ShouldAlert("退货", "box1", t.AddSeconds(31)));
    }
}
