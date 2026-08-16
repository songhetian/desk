using System;

namespace WordGuard.Client;

/// <summary>悬浮球状态：常态（监控中）/ 告警（命中违禁词）/ 离线或异常。</summary>
public enum OrbState
{
    Normal,
    Alert,
    Offline,
}

/// <summary>
/// 悬浮球状态机：把监控引擎事件映射为球的视觉三态。
/// <list type="bullet">
///   <item>离线优先：一旦 <see cref="SetOnline"/>(false)，无论是否命中均显示离线，直至恢复在线；</item>
///   <item>告警短促：<see cref="PulseAlert"/> 后红光仅维持 <see cref="_alertDuration"/>（默认 3s）即自动回常态，不常亮，守住"不打扰"；</item>
///   <item>离线期间产生的脉冲不残留，恢复在线即从常态起算。</item>
/// </list>
/// 纯逻辑、注入时钟无关（调用方传入 <c>now</c>），便于测试。
/// </summary>
public sealed class OrbStateController
{
    private readonly TimeSpan _alertDuration;
    private bool _online = true;
    private DateTime _alertUntil = DateTime.MinValue;

    public OrbStateController(TimeSpan alertDuration) => _alertDuration = alertDuration;

    /// <summary>标记词库/引擎是否在线。false → 立刻进入离线态；true → 退出离线态。</summary>
    public void SetOnline(bool online) => _online = online;

    /// <summary>命中违禁词时调用，使球进入告警态直至 <paramref name="now"/> + 告警时长。</summary>
    /// <param name="now">当前时刻（应为 UTC；内部按 UTC 归一，调用方混用本地时间也不会出错）。</param>
    public void PulseAlert(DateTime now)
    {
        if (!_online)
            return; // 离线期间脉冲忽略
        _alertUntil = now.ToUniversalTime() + _alertDuration;
    }

    /// <summary>返回当前悬浮球状态。</summary>
    /// <param name="now">当前时刻（应为 UTC；内部按 UTC 归一）。</param>
    public OrbState CurrentState(DateTime now)
    {
        if (!_online)
            return OrbState.Offline;
        return now.ToUniversalTime() < _alertUntil ? OrbState.Alert : OrbState.Normal;
    }
}
