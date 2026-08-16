using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class AhoCorasickMatcherTests
{
    [Fact]
    public void Finds_all_enabled_words_with_positions()
    {
        // 中文无词间空格，属于子串匹配；期望一次扫描拿到所有命中及其位置
        var words = new[]
        {
            new WordEntry { Text = "绝对", Enabled = true },
            new WordEntry { Text = "违禁词", Enabled = true },
        };
        var matcher = new AhoCorasickMatcher(words);

        var hits = matcher.Match("这是绝对违禁词内容");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Word == "绝对" && h.Index == 2 && h.Length == 2);
        Assert.Contains(hits, h => h.Word == "违禁词" && h.Index == 4 && h.Length == 3);
    }
}
