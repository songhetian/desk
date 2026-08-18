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

    [Fact]
    public void Expired_cooldown_entries_are_cleaned_up_on_access()
    {
        // 冷却过期后，条目应被清理，不会无限占用内存
        var d = new AlertDedup(TimeSpan.FromSeconds(30));
        var t = new DateTime(2026, 1, 1, 10, 0, 0);

        // 加入一批词并触发告警
        for (int i = 0; i < 50; i++)
            d.ShouldAlert("词" + i, "box1", t);

        // 初始 50 个词都在冷却中
        // 时间推进到冷却后，再访问一个新词，应触发清理
        var later = t.AddSeconds(60);
        d.ShouldAlert("新词", "box1", later);

        // 验证：清理后冷却条目数不应无限增长
        // （通过"再加 50 个新词后，总数大致等于活跃的数量"来间接验证）
        for (int i = 50; i < 100; i++)
            d.ShouldAlert("词" + i, "box1", later);

        // 总活跃冷却条目应该在合理范围内（约 51 个：50个新的 + 1个"新词"）
        // 而不是 101 个（旧的没被清掉）
        Assert.True(d.CooldownEntryCount <= 60,
            $"冷却条目 {d.CooldownEntryCount} 个，超过预期上限 60，说明过期条目没被清理");
    }

    [Fact]
    public void Acknowledged_entries_respect_max_capacity()
    {
        // 已确认的条目超过容量上限时，应淘汰最久未访问的
        var d = new AlertDedup(TimeSpan.FromSeconds(30), maxAcknowledged: 10);
        var t = new DateTime(2026, 1, 1, 10, 0, 0);

        // 确认 15 个不同的词+框组合
        for (int i = 0; i < 15; i++)
            d.Acknowledge("词" + i, "box1");

        // 容量限制生效：不应该有 15 个都存着
        Assert.True(d.AcknowledgedEntryCount <= 10,
            $"已确认条目 {d.AcknowledgedEntryCount} 个，超过容量上限 10");

        // 最早被确认的几个应该被淘汰了，再次访问应该能告警
        // 冷却已过 + 确认被淘汰 = 应该告警
        var later = t.AddSeconds(60);
        Assert.True(d.ShouldAlert("词0", "box1", later),
            "最早被确认的条目应被淘汰，冷却过后应能再次告警");
    }

    [Fact]
    public void Acknowledged_lru_preserves_recently_accessed()
    {
        // LRU：最近访问的不会被淘汰
        var d = new AlertDedup(TimeSpan.FromSeconds(30), maxAcknowledged: 5);
        var t = new DateTime(2026, 1, 1, 10, 0, 0);

        // 依次确认 词0 ~ 词4
        for (int i = 0; i < 5; i++)
            d.Acknowledge("词" + i, "box1");

        // 再次访问"词0"（让它变成最近使用的）
        d.ShouldAlert("词0", "box1", t.AddSeconds(5)); // 在冷却内，会走到 ack 检查逻辑，更新访问时间

        // 再加入 词5，应该淘汰最久没用的（词1，而不是词0）
        d.Acknowledge("词5", "box1");

        var later = t.AddSeconds(60);
        // 词0 应该还在（最近访问过）
        Assert.False(d.ShouldAlert("词0", "box1", later),
            "最近访问的词0应仍在确认列表中");
        // 词1 应该被淘汰了
        Assert.True(d.ShouldAlert("词1", "box1", later),
            "最久未访问的词1应被淘汰");
    }
}
