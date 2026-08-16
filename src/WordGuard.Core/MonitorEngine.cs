using System;
using System.Collections.Generic;

namespace WordGuard.Core;

/// <summary>一次输入捕获的入参。</summary>
/// <param name="Text">从目标软件输入框读取到的已成型文本。</param>
/// <param name="TargetProcess">输入所属进程 EXE 名（如 cs.exe），用于判定是否为被监控软件。</param>
/// <param name="TargetProcessPath">输入所属进程完整路径（可选），与 <see cref="TargetSpec.ExePath"/> 做前缀匹配。</param>
/// <param name="ContextId">输入框标识（同一窗口同一框稳定不变），去重按「词 + 框」维度。</param>
/// <param name="Timestamp">捕获时刻（UTC）。</param>
public sealed record CaptureInput(string Text, string TargetProcess, string TargetProcessPath, string ContextId, DateTime Timestamp);

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
    private readonly AlertDedup _dedup;
    private readonly List<TargetSpec> _targets;
    private readonly Dictionary<string, string> _lastText = new();

    public MonitorEngine(IMatcher matcher, AlertDedup dedup, IEnumerable<TargetSpec> targets)
    {
        _matcher = matcher;
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
        if (!IsMonitored(input.TargetProcess, input.TargetProcessPath))
            return new CaptureResult(false, Array.Empty<TriggeredWord>());

        // 文本变更 → 清除该输入框的「已确认」抑制（PRD：确认后直至文本变更清除该词）
        if (_lastText.TryGetValue(input.ContextId, out var prev) && prev != input.Text)
            _dedup.Reset(input.ContextId);
        _lastText[input.ContextId] = input.Text;

        var hits = _matcher.Match(input.Text);

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
            var shouldAlert = _dedup.ShouldAlert(word, input.ContextId, input.Timestamp);
            triggered.Add(new TriggeredWord(word, agg.Severity, shouldAlert, agg.Pos, agg.Id));
        }
        return new CaptureResult(true, triggered);
    }

    /// <summary>标记某「词 + 输入框」已被客服确认（委托给去重器，抑制直至文本变更/新会话）。</summary>
    public void Acknowledge(string word, string context) => _dedup.Acknowledge(word, context);
}
