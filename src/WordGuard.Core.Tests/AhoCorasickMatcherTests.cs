using System.Diagnostics;
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

    [Fact]
    public void Large_library_build_and_match_stay_fast()
    {
        // 性能基线：1000 个词的词库，构建 < 50ms，单次匹配 < 2ms
        // Aho-Corasick 的匹配时间与文本长度线性相关，与词库大小无关
        var rng = new Random(42);
        var words = new List<WordEntry>();
        for (int i = 0; i < 1000; i++)
        {
            var len = rng.Next(2, 6);
            var chars = new char[len];
            for (int j = 0; j < len; j++)
                chars[j] = (char)('一' + rng.Next(0, 500));
            words.Add(new WordEntry { Text = new string(chars), Enabled = true, Severity = Severity.Medium });
        }
        // 加几个确定能命中的词
        words.Add(new WordEntry { Text = "测试词", Enabled = true, Severity = Severity.High });
        words.Add(new WordEntry { Text = "监控", Enabled = true, Severity = Severity.Medium });

        var sw = Stopwatch.StartNew();
        var matcher = new AhoCorasickMatcher(words);
        sw.Stop();
        var buildMs = sw.ElapsedMilliseconds;

        var text = "这是一段用于测试性能的文本，包含测试词和监控等关键字，其他都是普通内容没有意义的填充文字，用来模拟真实客服聊天的文本长度，大概一两百字左右的样子。";
        // 预热
        matcher.Match(text);

        sw.Restart();
        for (int i = 0; i < 100; i++)
            matcher.Match(text);
        sw.Stop();
        var avgMatchMs = sw.ElapsedMilliseconds / 100.0;

        // 断言：构建 < 500ms（宽松上限，CI 机器慢），单次匹配 < 10ms
        Assert.True(buildMs < 500, $"构建 1000 词词库耗时 {buildMs}ms，超过 500ms 上限");
        Assert.True(avgMatchMs < 10, $"100 字文本平均匹配耗时 {avgMatchMs:F2}ms，超过 10ms 上限");
    }

    [Fact]
    public void Empty_library_matches_nothing_and_does_not_throw()
    {
        var matcher = new AhoCorasickMatcher(Enumerable.Empty<WordEntry>());
        var hits = matcher.Match("任意文本内容");
        Assert.Empty(hits);
    }

    [Fact]
    public void All_disabled_words_result_in_no_hits()
    {
        var words = new[]
        {
            new WordEntry { Text = "退货", Enabled = false },
            new WordEntry { Text = "加微信", Enabled = false },
        };
        var matcher = new AhoCorasickMatcher(words);
        var hits = matcher.Match("可以退货也可以加微信");
        Assert.Empty(hits);
    }

    [Fact]
    public void Fuzzy_match_finds_word_with_spaces_between_chars()
    {
        var words = new[]
        {
            new WordEntry { Text = "包过", Enabled = true, MatchMode = MatchMode.FuzzyContains },
        };
        var matcher = new AhoCorasickMatcher(words);

        var hits = matcher.Match("这个包 过的内容");

        Assert.Single(hits);
        Assert.Equal("包过", hits[0].Word);
    }

    [Fact]
    public void Fuzzy_match_finds_word_with_special_chars_between()
    {
        var words = new[]
        {
            new WordEntry { Text = "包过", Enabled = true, MatchMode = MatchMode.FuzzyContains },
            new WordEntry { Text = "第一", Enabled = true, MatchMode = MatchMode.FuzzyContains },
        };
        var matcher = new AhoCorasickMatcher(words);

        var hits1 = matcher.Match("包*过测试");
        Assert.Contains(hits1, h => h.Word == "包过");

        var hits2 = matcher.Match("第+一测试");
        Assert.Contains(hits2, h => h.Word == "第一");

        var hits3 = matcher.Match("第 一 个");
        Assert.Contains(hits3, h => h.Word == "第一");
    }

    [Fact]
    public void Fuzzy_match_still_matches_exact_substring()
    {
        var words = new[]
        {
            new WordEntry { Text = "包过", Enabled = true, MatchMode = MatchMode.FuzzyContains },
        };
        var matcher = new AhoCorasickMatcher(words);

        var hits = matcher.Match("包过的内容");

        Assert.Single(hits);
        Assert.Equal("包过", hits[0].Word);
    }

    [Fact]
    public void Contains_mode_does_not_match_across_spaces()
    {
        var words = new[]
        {
            new WordEntry { Text = "包过", Enabled = true, MatchMode = MatchMode.Contains },
        };
        var matcher = new AhoCorasickMatcher(words);

        var hits = matcher.Match("包 过");

        Assert.Empty(hits);
    }
}
