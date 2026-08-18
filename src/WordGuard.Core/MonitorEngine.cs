using System;
using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>一次输入捕获的入参。</summary>
/// <param name="Text">从目标软件输入框读取到的已成型文本。</param>
/// <param name="TargetProcess">输入所属进程 EXE 名（如 cs.exe），用于判定是否为被监控软件。</param>
/// <param name="TargetProcessPath">输入所属进程完整路径（可选），与 <see cref="TargetSpec.ExePath"/> 做前缀匹配。</param>
/// <param name="ContextId">输入框标识（同一窗口同一框稳定不变），去重按「词 + 框」维度。</param>
/// <param name="Timestamp">捕获时刻（UTC）。</param>
/// <param name="IsPinyin">是否为拼音输入（键盘钩子兜底模式下为 true，此时会额外做拼音匹配）。</param>
public sealed record CaptureInput(
    string Text,
    string TargetProcess,
    string TargetProcessPath,
    string ContextId,
    DateTime Timestamp,
    bool IsPinyin = false);

/// <summary>单个命中词在本次捕获中的处理结果。</summary>
/// <param name="Word">命中的违禁词。</param>
/// <param name="Severity">严重度（来自词库条目）。</param>
/// <param name="ShouldAlert">经去重/冷却后，此刻是否应输出告警（false 表示被抑制）。</param>
/// <param name="Positions">该词在文本中的所有出现位置（用于高亮），每项 (起始下标, 长度)。</param>
/// <param name="Id">对应词库条目 Id（用于审计日志回指；聚合时取首次命中的条目）。</param>
public sealed record TriggeredWord(string Word, Severity Severity, bool ShouldAlert, IReadOnlyList<(int Start, int Length)> Positions, Guid Id = default);

/// <summary>一次捕获的整体结果。</summary>
/// <param name="IsMonitoredTarget">输入是否来自被监控软件；false 时直接忽略。</param>
/// <param name="Triggered">所有命中词的处理结果（含被抑制项）。</param>
public sealed record CaptureResult(bool IsMonitoredTarget, IReadOnlyList<TriggeredWord> Triggered);

/// <summary>
/// 监控编排引擎：客户端"大脑"。将<b>目标判定 → 多模匹配 → 告警去重</b>串成一次调用。
/// 不依赖 UI / UIA，纯逻辑可测；UIA 捕获到的文本喂进来即可。
/// </summary>
public sealed class MonitorEngine
{
    private readonly IMatcher _matcher;
    private readonly IMatcher? _pinyinMatcher;
    private readonly AlertDedup _dedup;
    private readonly List<TargetSpec> _targets;
    private readonly Dictionary<string, string> _lastText = new();
    private readonly Dictionary<string, string> _lastPinyinText = new();

    public MonitorEngine(IMatcher matcher, AlertDedup dedup, IEnumerable<TargetSpec> targets)
        : this(matcher, null, dedup, targets) { }

    public MonitorEngine(IMatcher matcher, IMatcher? pinyinMatcher, AlertDedup dedup, IEnumerable<TargetSpec> targets)
    {
        _matcher = matcher;
        _pinyinMatcher = pinyinMatcher;
        _dedup = dedup;
        _targets = targets.ToList();
    }

    /// <summary>
    /// 判定某进程是否为被监控目标：EXE 名必中，且若目标配置了可选路径，则进程路径需以其为前缀。
    /// EXE 名与路径均大小写不敏感。
    /// </summary>
    private bool IsMonitored(string exeName, string exePath)
    {
        foreach (var t in _targets)
        {
            if (!string.Equals(t.ExeName, exeName, StringComparison.OrdinalIgnoreCase))
                continue;
            // EXE 名命中；若要求路径约束，则进程路径需以配置路径为前缀
            if (string.IsNullOrWhiteSpace(t.ExePath))
                return true;
            if (!string.IsNullOrWhiteSpace(exePath) &&
                exePath.Replace('/', '\\').StartsWith(
                    t.ExePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public CaptureResult ProcessCapture(CaptureInput input)
    {
        return ProcessCapture(input, false);
    }

    /// <param name="skipDedup">true 时跳过去重/冷却（用于模拟测试，保证每次都触发）。</param>
    public CaptureResult ProcessCapture(CaptureInput input, bool skipDedup)
    {
        if (!IsMonitored(input.TargetProcess, input.TargetProcessPath))
            return new CaptureResult(false, Array.Empty<TriggeredWord>());

        IReadOnlyList<MatchHit> hits;
        bool isPinyinMode = input.IsPinyin && _pinyinMatcher is not null;

        if (isPinyinMode)
        {
            // 拼音模式：用拼音匹配器匹配
            var pinyinText = input.Text.ToLowerInvariant();
            // 文本变更 → 清除该输入框的「已确认」抑制
            if (_lastPinyinText.TryGetValue(input.ContextId, out var prevPy) && prevPy != pinyinText)
                _dedup.Reset(input.ContextId);
            _lastPinyinText[input.ContextId] = pinyinText;
            hits = _pinyinMatcher!.Match(pinyinText);
        }
        else
        {
            // 普通模式：原文本匹配
            if (_lastText.TryGetValue(input.ContextId, out var prev) && prev != input.Text)
                _dedup.Reset(input.ContextId);
            _lastText[input.ContextId] = input.Text;
            hits = _matcher.Match(input.Text);
        }

        // 同一词在文本中可能多次出现、或在词库中对应多条条目：聚合位置并取最高严重度
        var byWord = new Dictionary<string, (Severity Severity, List<(int Start, int Length)> Pos, Guid Id)>();
        foreach (var h in hits)
        {
            if (!byWord.TryGetValue(h.Word, out var agg))
                agg = (h.Entry.Severity, new List<(int Start, int Length)>(), h.Entry.Id);
            if (h.Entry.Severity > agg.Severity)
                agg.Severity = h.Entry.Severity;
            agg.Pos.Add((h.Index, h.Length));
            byWord[h.Word] = agg;
        }

        var triggered = new List<TriggeredWord>();
        foreach (var (word, agg) in byWord)
        {
            // 拼音模式（键盘钩子兜底）不刷新冷却窗口：避免拼音缓冲区提前命中后，
            // 等中文真正上屏时反而被冷却抑制（如分开输入"第"+"一"的场景）。
            var refreshCooldown = !isPinyinMode;
            var shouldAlert = skipDedup || _dedup.ShouldAlert(word, input.ContextId, input.Timestamp, refreshCooldown);
            triggered.Add(new TriggeredWord(word, agg.Severity, shouldAlert, agg.Pos, agg.Id));
        }
        return new CaptureResult(true, triggered);
    }

    /// <summary>标记某「词 + 输入框」已被客服确认（委托给去重器，抑制直至文本变更/新会话）。</summary>
    public void Acknowledge(string word, string context) => _dedup.Acknowledge(word, context);
}
