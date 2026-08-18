using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class WordEntryTests
{
    [Fact]
    public void Default_match_mode_is_fuzzy_contains()
    {
        var e = new WordEntry { Text = "退货" };
        Assert.Equal(MatchMode.FuzzyContains, e.MatchMode);
    }

    [Fact]
    public void Default_id_is_generated_non_empty()
    {
        var e = new WordEntry { Text = "退货" };
        Assert.NotEqual(Guid.Empty, e.Id);
    }

    [Fact]
    public void Serialize_round_trip_preserves_id_and_match_mode()
    {
        var lib = new WordLibrary
        {
            Words =
            {
                new WordEntry { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Text = "退货", Category = "诱导", Severity = Severity.High, MatchMode = MatchMode.Contains },
            },
        };

        var json = lib.ToJson();
        var back = WordLibrary.Load(json);

        var reloaded = Assert.Single(back.Words);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), reloaded.Id);
        Assert.Equal(MatchMode.Contains, reloaded.MatchMode);
        Assert.Equal("退货", reloaded.Text);
    }

    [Fact]
    public void Legacy_json_without_id_or_match_mode_gets_defaults()
    {
        // 兼容旧版词库：缺 id / matchMode 时补默认值，不抛异常
        var json = "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"severity\":\"medium\"}]}";
        var lib = WordLibrary.Load(json);

        var e = Assert.Single(lib.Words);
        Assert.Equal(MatchMode.FuzzyContains, e.MatchMode);
        Assert.NotEqual(Guid.Empty, e.Id);
    }
}
