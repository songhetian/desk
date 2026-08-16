namespace WordGuard.Core;

/// <summary>违禁词严重级别，可用于告警分级展示。</summary>
public enum Severity
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>匹配模式（PRD 数据契约 matchMode）。当前仅支持子串包含；后续可扩展整词/正则。</summary>
public enum MatchMode
{
    /// <summary>子串包含匹配（默认）。</summary>
    Contains = 0,
}

/// <summary>
/// 词库中的一条违禁词条目。
/// </summary>
public sealed record WordEntry
{
    /// <summary>稳定标识（UUID）。用于审计日志回指与去重关联；缺省时自动生成。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>违禁词文本（匹配依据）。</summary>
    public string Text { get; init; } = "";

    /// <summary>分类，便于管理端分组与筛选（如「夸大宣传」「诱导」）。</summary>
    public string Category { get; init; } = "";

    /// <summary>严重级别。</summary>
    public Severity Severity { get; init; } = Severity.Medium;

    /// <summary>匹配模式，默认子串包含。</summary>
    public MatchMode MatchMode { get; init; } = MatchMode.Contains;

    /// <summary>是否启用；禁用的词不参与匹配。</summary>
    public bool Enabled { get; init; } = true;
}
