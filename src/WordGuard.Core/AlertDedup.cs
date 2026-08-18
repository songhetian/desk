using System;
using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>
/// 告警去重与冷却：避免同一违禁词反复刷屏。
/// <list type="bullet">
///   <item><b>跨框共享冷却窗口</b>：同一词在冷却窗口（默认 30s）内，无论哪个输入框命中都被抑制，实现 PRD「跨输入框的同词也遵循去重窗口，避免刷屏」；</item>
///   <item><b>按框确认抑制</b>：客服在弹窗「确认」后，该<b>输入框</b>不再就同词告警，直至 <see cref="Reset(string,string)"/>（文本变更）或 <see cref="Reset(string)"/>（新会话）；</item>
///   <item><b>内存防护</b>：冷却过期条目自动清理；已确认条目超过容量时 LRU 淘汰，避免长时间运行内存无限增长；</item>
///   <item>所有状态访问均加锁，可安全用于 FileSystemWatcher 线程重写引擎与 UI 线程并发读取的场景。</item>
/// </list>
/// </summary>
public sealed class AlertDedup
{
    private readonly TimeSpan _cooldown;
    private readonly int _maxAcknowledged;
    private readonly object _gate = new();

    /// <summary>跨框共享的冷却截止时间（按词）：命中即刷新，抑制所有框的同词再告警。</summary>
    private readonly Dictionary<string, DateTime> _cooldownUntil = new(StringComparer.Ordinal);

    /// <summary>按「词 + 输入框」的已确认抑制标记（LRU 链表：头=最久未访问，尾=最近访问）。</summary>
    private readonly Dictionary<(string Word, string Context), LinkedListNode<(string Word, string Context)>> _ackMap = new();
    private readonly LinkedList<(string Word, string Context)> _ackLru = new();

    /// <summary>冷却条目数（用于测试与诊断）。</summary>
    public int CooldownEntryCount { get { lock (_gate) return _cooldownUntil.Count; } }

    /// <summary>已确认条目数（用于测试与诊断）。</summary>
    public int AcknowledgedEntryCount { get { lock (_gate) return _ackMap.Count; } }

    /// <summary>默认已确认条目容量上限。</summary>
    public const int DefaultMaxAcknowledged = 1000;

    public AlertDedup(TimeSpan cooldown) : this(cooldown, DefaultMaxAcknowledged) { }

    public AlertDedup(TimeSpan cooldown, int maxAcknowledged)
    {
        _cooldown = cooldown;
        _maxAcknowledged = maxAcknowledged > 0 ? maxAcknowledged : DefaultMaxAcknowledged;
    }

    /// <summary>
    /// 判断此刻是否应对该「词 + 输入框」触发告警。命中跨框冷却或已确认则返回 false（抑制）。
    /// <paramref name="now"/> 应为 UTC（内部按 UTC 归一，调用方混用本地时间也不会出错）。
    /// <paramref name="refreshCooldown">是否刷新冷却窗口（默认 true）。兜底模式（如拼音匹配）可设为 false，避免提前命中后抑制主路径的正常告警。</paramref>
    /// </summary>
    public bool ShouldAlert(string word, string context, DateTime now, bool refreshCooldown = true)
    {
        now = now.ToUniversalTime();
        var key = (word, context);

        lock (_gate)
        {
            // 惰性清理过期冷却条目：每次访问顺手清一批，避免内存无限增长
            CleanupExpiredCooldown(now);

            // 已确认 → 抑制，但更新 LRU 访问时间
            if (_ackMap.TryGetValue(key, out var node))
            {
                _ackLru.Remove(node);
                _ackLru.AddLast(node);
                return false;
            }

            // 冷却中 → 抑制
            if (_cooldownUntil.TryGetValue(word, out var until) && now < until)
                return false;

            // 刷新跨框冷却窗口（兜底模式可跳过，避免提前命中影响主路径）
            if (refreshCooldown)
                _cooldownUntil[word] = now + _cooldown;
            return true;
        }
    }

    /// <summary>标记该「词 + 输入框」已被客服确认，此后抑制直至 Reset。</summary>
    public void Acknowledge(string word, string context)
    {
        var key = (word, context);
        lock (_gate)
        {
            if (_ackMap.TryGetValue(key, out var existing))
            {
                // 已存在：移到尾部（最近访问）
                _ackLru.Remove(existing);
                _ackLru.AddLast(existing);
                return;
            }

            // 新增：检查容量，超限则淘汰最久未访问的
            if (_ackMap.Count >= _maxAcknowledged)
            {
                var oldest = _ackLru.First!;
                _ackLru.RemoveFirst();
                _ackMap.Remove(oldest.Value);
            }

            var node = _ackLru.AddLast(key);
            _ackMap[key] = node;
        }
    }

    /// <summary>清除该「词 + 输入框」的冷却 / 确认状态（文本变更时调用）。</summary>
    public void Reset(string word, string context)
    {
        var key = (word, context);
        lock (_gate)
        {
            if (_ackMap.TryGetValue(key, out var node))
            {
                _ackLru.Remove(node);
                _ackMap.Remove(key);
            }
        }
    }

    /// <summary>清除某个输入框的全部「已确认」抑制（开启新会话时调用）。</summary>
    public void Reset(string context)
    {
        lock (_gate)
        {
            var toRemove = new List<(string, string)>();
            foreach (var key in _ackMap.Keys)
                if (key.Context == context)
                    toRemove.Add(key);
            foreach (var key in toRemove)
            {
                var node = _ackMap[key];
                _ackLru.Remove(node);
                _ackMap.Remove(key);
            }
        }
    }

    /// <summary>惰性清理过期的冷却条目。调用方需持有锁。</summary>
    private void CleanupExpiredCooldown(DateTime now)
    {
        // 小批量清理：每次最多扫 20 条，避免单次操作耗时过长
        var removed = 0;
        foreach (var key in new List<string>(_cooldownUntil.Keys))
        {
            if (removed >= 20) break;
            if (_cooldownUntil.TryGetValue(key, out var until) && now >= until)
            {
                _cooldownUntil.Remove(key);
                removed++;
            }
        }
    }
}
