using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

public class AlertDispatcherTests
{
    private static LibraryMetadata Meta(bool popup = true, bool sound = true, bool highlight = true)
        => new() { AlertPopup = popup, AlertSound = sound, AlertHighlight = highlight };

    [Fact]
    public void Dispatch_with_alerting_word_and_all_channels_enabled_fires_all_channels()
    {
        var dispatcher = new AlertDispatcher(Meta()); // 三通道默认全开

        var result = new CaptureResult(true, new[]
        {
            new TriggeredWord("违禁词", Severity.High, ShouldAlert: true, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        Assert.True(evt.HasAlert);
        Assert.Contains(AlertChannel.Popup, evt.Channels);
        Assert.Contains(AlertChannel.Sound, evt.Channels);
        Assert.Contains(AlertChannel.Highlight, evt.Channels);
        Assert.Contains("违禁词", evt.AlertWords);
        Assert.Equal(Severity.High, evt.TopSeverity);
    }

    [Fact]
    public void Dispatch_for_non_monitored_target_yields_no_alert()
    {
        var dispatcher = new AlertDispatcher(Meta());

        // IsMonitoredTarget=false：即便文本含命中词也应完全忽略（PRD：非目标软件零打扰）
        var result = new CaptureResult(false, new[]
        {
            new TriggeredWord("违禁词", Severity.High, ShouldAlert: true, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        Assert.False(evt.HasAlert);
        Assert.Empty(evt.Channels);
    }

    [Fact]
    public void Dispatch_when_all_words_suppressed_yields_no_alert()
    {
        var dispatcher = new AlertDispatcher(Meta());

        // 命中词全部被去重/确认抑制（ShouldAlert=false）：不应触发任何通道（PRD 防刷屏）
        var result = new CaptureResult(true, new[]
        {
            new TriggeredWord("违禁词", Severity.High, ShouldAlert: false, Array.Empty<(int, int)>()),
            new TriggeredWord("敏感词", Severity.Medium, ShouldAlert: false, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        Assert.False(evt.HasAlert);
        Assert.Empty(evt.Channels);
    }

    [Fact]
    public void Dispatch_respects_channel_toggles()
    {
        var dispatcher = new AlertDispatcher(Meta(popup: false, sound: true, highlight: true));

        var result = new CaptureResult(true, new[]
        {
            new TriggeredWord("违禁词", Severity.Medium, ShouldAlert: true, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        Assert.True(evt.HasAlert);
        Assert.DoesNotContain(AlertChannel.Popup, evt.Channels);
        Assert.Contains(AlertChannel.Sound, evt.Channels);
        Assert.Contains(AlertChannel.Highlight, evt.Channels);
    }

    [Fact]
    public void Dispatch_with_all_channels_off_still_records_match_but_fires_nothing()
    {
        var dispatcher = new AlertDispatcher(Meta(popup: false, sound: false, highlight: false));

        var result = new CaptureResult(true, new[]
        {
            new TriggeredWord("违禁词", Severity.High, ShouldAlert: true, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        // 命中仍记录（供审计），但没有任何可见通道
        Assert.True(evt.HasAlert);
        Assert.Empty(evt.Channels);
        Assert.Contains("违禁词", evt.AlertWords);
    }

    [Fact]
    public void Dispatch_takes_highest_severity_and_all_active_words()
    {
        var dispatcher = new AlertDispatcher(Meta());

        var result = new CaptureResult(true, new[]
        {
            new TriggeredWord("低危词", Severity.Low, ShouldAlert: true, Array.Empty<(int, int)>()),
            new TriggeredWord("高危词", Severity.High, ShouldAlert: true, Array.Empty<(int, int)>()),
            new TriggeredWord("中危词", Severity.Medium, ShouldAlert: true, Array.Empty<(int, int)>()),
        });

        var evt = dispatcher.Dispatch(result);

        Assert.Equal(Severity.High, evt.TopSeverity);
        Assert.Equal(3, evt.ActiveWords.Count);
        Assert.Contains("高危词", evt.AlertWords);
        Assert.Contains("低危词", evt.AlertWords);
        Assert.Contains("中危词", evt.AlertWords);
    }
}
