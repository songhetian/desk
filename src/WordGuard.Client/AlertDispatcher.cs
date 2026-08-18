using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>告警通道（PRD：弹窗 / 声音 / 语音播报 / 自有界面高亮，四项独立开关）。</summary>
public enum AlertChannel
{
    Popup,
    Sound,
    Voice,
    Highlight,
}

/// <summary>一次捕获经派遣后的告警结论。</summary>
/// <param name="HasAlert">是否存在「应告警」的命中词（独立于通道开关；即便所有通道关闭也记为命中，供审计）。</param>
/// <param name="Channels">按当前配置实际应触发的通道集合（可能为空）。</param>
/// <param name="AlertWords">应告警的命中词文本（用于高亮与弹窗展示）。</param>
/// <param name="TopSeverity">命中词中的最高严重度（用于分级展示）。</param>
/// <param name="ActiveWords">应告警的命中词完整信息（含高亮位置）。</param>
public sealed record AlertEvent(
    bool HasAlert,
    IReadOnlyList<AlertChannel> Channels,
    IReadOnlyList<string> AlertWords,
    Severity TopSeverity,
    IReadOnlyList<TriggeredWord> ActiveWords)
{
    /// <summary>未命中任何应告警词的结论（非目标软件或全被去重/确认抑制）。</summary>
    public static readonly AlertEvent None = new(false, Array.Empty<AlertChannel>(), Array.Empty<string>(), Severity.Low, Array.Empty<TriggeredWord>());
}

/// <summary>
/// 告警派遣：把监控引擎的 <see cref="CaptureResult"/> 与当前词库下发的 <see cref="LibraryMetadata"/> 翻译成
/// 「实际触发哪些通道 + 高亮哪些词 + 最高严重度」。纯逻辑，UI 弹窗/声音/高亮据此执行，互不耦合。
///
/// <para>三通道开关来自词库 metadata（管理员锁定、随词库下发、客户端只读），不再来自本地 AppSettings。</para>
/// </summary>
public sealed class AlertDispatcher
{
    private readonly LibraryMetadata _metadata;

    public AlertDispatcher(LibraryMetadata metadata) => _metadata = metadata;

    public AlertEvent Dispatch(CaptureResult result)
    {
        if (!result.IsMonitoredTarget)
            return AlertEvent.None;

        var active = result.Triggered.Where(t => t.ShouldAlert).ToList();
        if (active.Count == 0)
            return AlertEvent.None;

        var channels = new List<AlertChannel>();
        if (_metadata.AlertPopup) channels.Add(AlertChannel.Popup);
        if (_metadata.AlertSound) channels.Add(AlertChannel.Sound);
        if (_metadata.AlertVoice) channels.Add(AlertChannel.Voice);
        if (_metadata.AlertHighlight) channels.Add(AlertChannel.Highlight);

        var top = active.Max(t => t.Severity);
        return new AlertEvent(
            true,
            channels,
            active.Select(t => t.Word).ToList(),
            top,
            active);
    }
}
