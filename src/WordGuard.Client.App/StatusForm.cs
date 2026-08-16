using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控状态面板（WebView2 渲染原型 HTML）：只读展示监控目标、三通道开关、监控词库、运行信息。
/// 数据来自 <see cref="LibraryFileSource.Metadata"/>（管理员随词库下发，本端只读）。
///
/// 员工可看到「在监控哪些词」（信息透明），但无法修改任何配置——这是配置锁定下的合理边界。
/// </summary>
public sealed class StatusForm : HtmlWindow
{
    private readonly LibraryFileSource _libSource;
    private readonly string _libPath;

    public StatusForm(LibraryFileSource libSource, string libPath) : base("status.html")
    {
        _libSource = libSource;
        _libPath = libPath;
        Text = "监控状态 — WordGuard";
        Size = new Size(560, 720);
        MinimumSize = new Size(440, 620);
    }

    protected override void OnJsMessage(string json)
    {
        if (!json.Contains("ready")) return;
        PushInit();
    }

    private void PushInit()
    {
        var m = _libSource.Metadata;
        var online = _libSource.Status.FileExists;

        // 词库条目（只读展示在状态面板）
        var words = System.Array.Empty<object>();
        string updatedAt = "—";
        if (online)
        {
            try
            {
                var lib = Core.WordLibrary.LoadFromFile(_libPath);
                words = lib.Words.Select(w => new
                {
                    w.Text,
                    Severity = w.Severity switch { Severity.High => "high", Severity.Medium => "medium", _ => "low" },
                    w.Enabled,
                }).ToArray();
                updatedAt = lib.UpdatedAt == DateTime.MinValue
                    ? "—"
                    : lib.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch { /* 损坏文件：忽略 */ }
        }

        PostToJs(Json(new
        {
            type = "init",
            online,
            targets = m.Targets.Select(t => new { t.ExeName, t.ExePath }),
            alertPopup = m.AlertPopup,
            alertSound = m.AlertSound,
            alertHighlight = m.AlertHighlight,
            libStatus = online ? "已加载" : "未找到词库文件",
            words,
            cooldownSeconds = m.CooldownSeconds,
            logRetentionDays = m.LogRetentionDays,
            libPath = _libPath,
            updatedAt,
        }));
    }
}
