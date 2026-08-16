using System;
using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>
/// 告警去重与冷却：避免同一违禁词反复刷屏。
/// <list type="bullet">
///   <item><b>跨框共享冷却窗口</b>：同一词在冷却窗口（默认 30s）内，无论哪个输入框命中都被抑制，实现 PRD「跨输入框的同词也遵循去重窗口，避免刷屏」；</item>
///   <item><b>按框确认抑制</b>：客服在弹窗「确认」后，该<b>输入框</b>不再就同词告警，直至 <see cref="Reset(string,string)"/>（文本变更）或 <see cref="Reset(string)"/>（新会话）；</item>
///   <item>所有状态访问均加锁，可安全用于 FileSystemWatcher 线程重写引擎与 UI 线程并发读取的场景。</item>
/// </list>
/// </summary>
public sealed class AlertDedup
{
    private readonly TimeSpan _cooldown;
    private readonly object _gate = new();

    /// <summary>跨框共享的冷却截止时间（按词）：命中即刷新，抑制所有框的同词再告警。</summary>
    private readonly Dictionary<string, DateTime> _cooldownUntil = new(StringComparer.Ordinal);

    /// <summary>按「词 + 输入框」的已确认抑制标记。</summary>
    private readonly Dictionary<(string Word, string Context), bool> _acknowledged = new();

    public AlertDedup(TimeSpan cooldown) => _cooldown = cooldown;

    /// <summary>
    /// 判断此刻是否应对该「词 + 输入框」触发告警。命中跨框冷却或已确认则返回 false（抑制）。
    /// <paramref name="now"/> 应为 UTC（内部按 UTC 归一，调用方混用本地时间也不会出错）。
    /// </summary>
    public bool ShouldAlert(string word, string context, DateTime now)
    {
        now = now.ToUniversalTime();
        lock (_gate)
        {
            if (_acknowledged.TryGetValue((word, context), out var ack) && ack)
                return false;
            if (_cooldownUntil.TryGetValue(word, out var until) && now < until)
                return false;

            _cooldownUntil[word] = now + _cooldown; // 刷新跨框冷却窗口
            return true;
        }
    }

    /// <summary>标记该「词 + 输入框」已被客服确认，此后抑制直至 Reset。</summary>
    public void Acknowledge(string word, string context)
    {
        lock (_gate) _acknowledged[(word, context)] = true;
    }

    /// <summary>清除该「词 + 输入框」的冷却 / 确认状态（文本变更时调用）。</summary>
    public void Reset(string word, string context)
    {
        lock (_gate) _acknowledged.Remove((word, context));
    }

    /// <summary>清除某个输入框的全部「已确认」抑制（开启新会话时调用）。</summary>
    public void Reset(string context)
    {
        lock (_gate)
        {
            foreach (var key in new List<(string, string)>(_acknowledged.Keys))
                if (key.Item2 == context)
                    _acknowledged.Remove(key);
        }
    }
}
