using WordGuard.Client;
using Xunit;

namespace WordGuard.Client.Tests;

public class OrbStateControllerTests
{
    [Fact]
    public void Default_state_is_normal()
    {
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        Assert.Equal(OrbState.Normal, c.CurrentState(DateTime.UtcNow));
    }

    [Fact]
    public void Alert_returns_to_normal_after_duration()
    {
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        c.PulseAlert(t);
        Assert.Equal(OrbState.Alert, c.CurrentState(t));            // 命中瞬间红光
        Assert.Equal(OrbState.Alert, c.CurrentState(t.AddSeconds(2)));
        Assert.Equal(OrbState.Normal, c.CurrentState(t.AddSeconds(3))); // 3s 后回常态（短促不常亮）
    }

    [Fact]
    public void Going_offline_takes_precedence_over_alert()
    {
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        c.PulseAlert(t);
        c.SetOnline(false);
        Assert.Equal(OrbState.Offline, c.CurrentState(t)); // 离线优先，哪怕刚命中
    }

    [Fact]
    public void Back_online_returns_to_normal()
    {
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        c.SetOnline(false);
        c.SetOnline(true);
        Assert.Equal(OrbState.Normal, c.CurrentState(t));
    }

    [Fact]
    public void Pulse_while_offline_is_ignored()
    {
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        var t = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        c.SetOnline(false);
        c.PulseAlert(t);
        Assert.Equal(OrbState.Offline, c.CurrentState(t));
        c.SetOnline(true);
        Assert.Equal(OrbState.Normal, c.CurrentState(t)); // 离线期间的脉冲不残留
    }

    [Fact]
    public void Mixed_datetime_kind_does_not_break_alert_window()
    {
        // 评审遗留：调用方若混用 UTC 与本地时间，未归一会导致告警窗口误判。
        // 归一为 UTC 后，同一时刻的不同 Kind 应得到一致结果。
        var c = new OrbStateController(TimeSpan.FromSeconds(3));
        var utc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var localSameInstant = utc.ToLocalTime(); // 同一瞬间，但 Kind=Local

        c.PulseAlert(utc);
        Assert.Equal(OrbState.Alert, c.CurrentState(localSameInstant)); // 归一 UTC → 仍为告警
    }
}
