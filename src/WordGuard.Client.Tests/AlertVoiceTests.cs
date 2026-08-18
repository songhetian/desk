using System.Collections.Generic;
using WordGuard.Client;
using Xunit;

namespace WordGuard.Client.Tests;

public class AlertVoiceTests
{
    [Fact]
    public void BuildMessage_includes_every_alert_word_and_category()
    {
        var words = new List<string> { "退款", "加微信" };

        var msg = AlertVoice.BuildMessage(words, "广告导流");

        Assert.Contains("退款", msg);
        Assert.Contains("加微信", msg);
        Assert.Contains("广告导流", msg);
        Assert.False(string.IsNullOrWhiteSpace(msg));
    }

    [Fact]
    public void BuildMessage_enumerates_multiple_words_with_separator()
    {
        var words = new List<string> { "最低价", "包过", "保证" };

        var msg = AlertVoice.BuildMessage(words, "价格违规");

        Assert.Contains("最低价", msg);
        Assert.Contains("包过", msg);
        Assert.Contains("保证", msg);
        Assert.Contains("价格违规", msg);
    }

    [Fact]
    public void BuildMessage_handles_empty_words_and_missing_category_gracefully()
    {
        var msg = AlertVoice.BuildMessage(new List<string>(), null);

        Assert.False(string.IsNullOrWhiteSpace(msg));
        Assert.Contains("违禁词", msg);
    }
}
