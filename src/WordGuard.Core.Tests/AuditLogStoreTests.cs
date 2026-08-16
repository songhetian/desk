using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class AuditLogStoreTests
{
    [Fact]
    public void Query_by_time_range_returns_only_entries_in_range()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        var t1 = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc); // 范围外
        var t3 = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Add(Mk(t1, "cs.exe", "可以退货"));
        store.Add(Mk(t2, "cs.exe", "加微信"));
        store.Add(Mk(t3, "qq.exe", "绝对保真"));

        var from = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 1, 13, 0, 0, DateTimeKind.Utc);
        var result = store.Query(from, to);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.TriggeredContent == "可以退货");
        Assert.Contains(result, e => e.TriggeredContent == "绝对保真");
        Assert.DoesNotContain(result, e => e.TriggeredContent == "加微信");
    }

    [Fact]
    public void Content_filter_narrows_results()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        var baseT = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Add(Mk(baseT, "cs.exe", "可以退货"));
        store.Add(Mk(baseT, "cs.exe", "加微信聊"));
        store.Add(Mk(baseT, "cs.exe", "绝对保真"));

        var result = store.Query(baseT.AddHours(-1), baseT.AddHours(1), contentFilter: "微信");

        Assert.Single(result);
        Assert.Equal("加微信聊", result[0].TriggeredContent);
    }

    [Fact]
    public void Results_ordered_newest_first()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        store.Add(Mk(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc), "cs.exe", "早"));
        store.Add(Mk(new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc), "cs.exe", "晚"));
        store.Add(Mk(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), "cs.exe", "中"));

        var result = store.Query(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { "晚", "中", "早" }, result.Select(e => e.TriggeredContent));
    }

    [Fact]
    public void Prune_removes_entries_older_than_cutoff()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        store.Add(Mk(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), "cs.exe", "旧")); // 应被清理
        store.Add(Mk(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), "cs.exe", "新"));

        store.PruneOlderThan(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = store.Query(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        Assert.Single(result);
        Assert.Equal("新", result[0].TriggeredContent);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Triggered_at_stored_as_iso8601_and_round_trips()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        var ts = new DateTime(2026, 8, 1, 9, 30, 15, DateTimeKind.Utc);
        store.Add(Mk(ts, "cs.exe", "x"));

        var result = store.Query(ts.AddMinutes(-1), ts.AddMinutes(1));
        var e = Assert.Single(result);
        Assert.Equal(DateTimeKind.Utc, e.Timestamp.Kind);
        Assert.Equal(ts, e.Timestamp);
    }

    [Fact]
    public void Window_title_and_alert_channels_persisted()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        var ts = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Add(new AuditLogEntry
        {
            Timestamp = ts,
            TargetSoftware = "cs.exe",
            TriggeredContent = "可以退货",
            MatchedWords = new List<MatchedWord>(),
            Severity = Severity.Medium,
            Disposition = "已确认",
            WindowTitle = "客户会话 - 张三",
            AlertChannels = "popup,sound",
        });

        var result = store.Query(ts.AddMinutes(-1), ts.AddMinutes(1));
        var e = Assert.Single(result);
        Assert.Equal("客户会话 - 张三", e.WindowTitle);
        Assert.Equal("popup,sound", e.AlertChannels);
    }

    [Fact]
    public void Matched_words_round_trip_with_id_and_text()
    {
        using var store = new AuditLogStore("Data Source=:memory:");
        var ts = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Add(new AuditLogEntry
        {
            Timestamp = ts,
            TargetSoftware = "cs.exe",
            TriggeredContent = "可以退货也可加微信",
            MatchedWords = new List<MatchedWord>
            {
                new("11111111-1111-1111-1111-111111111111", "退货"),
                new("22222222-2222-2222-2222-222222222222", "加微信"),
            },
            Severity = Severity.High,
            Disposition = "已记录",
        });

        var result = store.Query(ts.AddMinutes(-1), ts.AddMinutes(1));
        var words = Assert.Single(result).MatchedWords;
        Assert.Equal(2, words.Count);
        Assert.Equal("退货", words[0].Text);
        Assert.Equal("11111111-1111-1111-1111-111111111111", words[0].Id);
        Assert.Equal("加微信", words[1].Text);
    }

    private static AuditLogEntry Mk(DateTime ts, string target, string content) => new()
    {
        Timestamp = ts,
        TargetSoftware = target,
        TriggeredContent = content,
        MatchedWords = new List<MatchedWord>(),
        Severity = Severity.Medium,
        Disposition = "已记录",
        WindowTitle = "",
        AlertChannels = "",
    };
}
