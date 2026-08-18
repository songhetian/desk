using System.Speech.Synthesis;

namespace WordGuard.Client.App;

/// <summary>
/// 语音播报：命中违禁词时用 Windows SAPI（<see cref="SpeechSynthesizer"/>）朗读告警文案。
/// 非阻塞（<see cref="SpeechSynthesizer.SpeakAsync"/>）；TTS 不可用（无语音包/设备故障）时静默降级，绝不拖垮监控主流程。
/// 文案由 <see cref="WordGuard.Client.AlertVoice.BuildMessage"/> 生成（纯函数、可单测）。
/// </summary>
public static class VoiceAnnouncer
{
    private static readonly object _lock = new();
    private static SpeechSynthesizer? _synth;
    private static bool _voiceResolved;

    public static void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            lock (_lock)
            {
                EnsureSynthesizer();
                // SpeakAsync 入队即返回，不阻塞 500ms 轮询循环
                _synth?.SpeakAsync(text);
            }
        }
        catch
        {
            // 朗读失败不应影响监控与弹窗
        }
    }

    private static void EnsureSynthesizer()
    {
        if (_voiceResolved) return;
        _synth = new SpeechSynthesizer();
        // 语速调快（-10 到 +10，默认 0，调到 +2 更自然不拖沓）
        _synth.Rate = 2;
        // 音量适中
        _synth.Volume = 90;

        // 优先选用中文语音（无则交由 SAPI 回退到系统默认，不抛异常）
        try
        {
            var all = _synth.GetInstalledVoices();
            var zh = all.FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture?.Name
                            .StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) == true)
                     ?? all.FirstOrDefault(v => v.Enabled);
            if (zh is not null) _synth.SelectVoice(zh.VoiceInfo.Name);
        }
        catch { /* 语音选择失败：保持默认 */ }
        _voiceResolved = true;
    }
}
