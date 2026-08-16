using System.IO;
using System.Media;

namespace WordGuard.Client.App;

/// <summary>告警声音：自定义 wav 优先，否则系统默认提示音（PRD 声音提醒通道）。</summary>
public static class AlertSound
{
    public static void Play(string? customPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                using var player = new SoundPlayer(customPath!);
                player.Play(); // 非阻塞
                return;
            }
            SystemSounds.Beep.Play();
        }
        catch
        {
            // 音频设备故障不应影响监控主流程
            try { SystemSounds.Beep.Play(); } catch { }
        }
    }
}
