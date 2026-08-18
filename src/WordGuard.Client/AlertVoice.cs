using System.Collections.Generic;
using System.Linq;

namespace WordGuard.Client;

/// <summary>
/// 语音告警文案构建（纯函数，与平台 TTS 实现解耦，便于单测）。
/// 真正的朗读由 <see cref="WordGuard.Client.App.VoiceAnnouncer"/>（Windows SAPI）执行。
/// </summary>
public static class AlertVoice
{
    /// <summary>把命中词与分类拼成一句口语化告警文案。</summary>
    public static string BuildMessage(IReadOnlyList<string>? words, string? category = null)
    {
        var list = (words ?? System.Array.Empty<string>())
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        if (list.Count == 0)
            return "请注意，检测到违禁词，请立即停止发送。";

        var wordStr = list.Count == 1
            ? $"「{list[0]}」"
            : $"{list.Count}个，{string.Join("、", list)}";

        var catStr = string.IsNullOrWhiteSpace(category) ? "" : $"，属于{category}类别";

        // 加入停顿感：用逗号分隔语义块，让 TTS 读起来更自然
        return $"请注意，检测到违禁词{wordStr}{catStr}。，请立即检查，不要发送。";
    }
}
