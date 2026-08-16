using System.Text.Json;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控状态面板（WebView2 渲染原型 HTML）。
/// 词库词条为只读展示（信息透明）；<b>部署配置（监控目标 / 三通道开关 / 去重 / 保留）本端可编辑</b>，
/// 保存后写入 <c>wordguard.settings.json</c> 的 <see cref="AppSettings.Deployment"/> 覆盖段并立即生效，
/// 覆盖管理端随 wordlib.json 下发的默认值。
/// </summary>
public sealed class StatusForm : HtmlWindow
{
    private readonly Func<LibraryFileSource> _getLib;
    private readonly Func<string> _getLibPath;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly Action _onSaved;

    public StatusForm(Func<LibraryFileSource> getLib, Func<string> getLibPath,
        AppSettings settings, string settingsPath, Action onSaved) : base("status.html")
    {
        _getLib = getLib;
        _getLibPath = getLibPath;
        _settings = settings;
        _settingsPath = settingsPath;
        _onSaved = onSaved;
        Text = "监控状态 — WordGuard";
        Size = new Size(560, 760);
        MinimumSize = new Size(440, 640);
    }

    protected override void OnJsMessage(string json)
    {
        string? type = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "ready": PushInit(); break;
                case "saveDeploy": SaveDeploy(doc); break;
                case "resetDeploy": ResetDeploy(); break;
            }
        }
        catch { /* 非 JSON 或字段缺失：忽略 */ }
    }

    private void SaveDeploy(JsonDocument doc)
    {
        var root = doc.RootElement;
        var targets = new List<string>();
        if (root.TryGetProperty("targets", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
            foreach (var t in tArr.EnumerateArray())
                if (t.ValueKind == JsonValueKind.String) targets.Add(t.GetString() ?? "");

        _settings.Deployment = new ClientDeployment
        {
            MonitorTargets = targets,
            AlertPopup = root.GetProperty("alertPopup").GetBoolean(),
            AlertSound = root.GetProperty("alertSound").GetBoolean(),
            AlertHighlight = root.GetProperty("alertHighlight").GetBoolean(),
            CooldownSeconds = root.GetProperty("cooldownSeconds").GetInt32(),
            LogRetentionDays = root.GetProperty("logRetentionDays").GetInt32(),
        };
        try
        {
            _settings.Save(_settingsPath);
            _onSaved();              // 重新应用覆盖并重建捕获宿主
            PushInit();              // 刷新面板显示生效值
            PostToJs(Json(new { type = "toast", text = "本机部署配置已保存并生效 ✓", ok = true }));
        }
        catch (System.Exception ex)
        {
            PostToJs(Json(new { type = "toast", text = $"保存失败：{ex.Message}", ok = false }));
        }
    }

    private void ResetDeploy()
    {
        _settings.Deployment = null;
        try
        {
            _settings.Save(_settingsPath);
            _onSaved();
            PushInit();
            PostToJs(Json(new { type = "toast", text = "已恢复为管理端下发默认值", ok = true }));
        }
        catch (System.Exception ex)
        {
            PostToJs(Json(new { type = "toast", text = $"保存失败：{ex.Message}", ok = false }));
        }
    }

    private void PushInit()
    {
        var lib = _getLib();
        var m = lib.Metadata;
        var online = lib.Status.FileExists;
        var libPath = _getLibPath();

        // 词库条目（只读展示在状态面板）
        var words = System.Array.Empty<object>();
        string updatedAt = "—";
        if (online)
        {
            try
            {
                var wl = Core.WordLibrary.LoadFromFile(libPath);
                words = wl.Words.Select(w => new
                {
                    w.Text,
                    Severity = w.Severity switch { Severity.High => "high", Severity.Medium => "medium", _ => "low" },
                    w.Enabled,
                }).ToArray();
                updatedAt = wl.UpdatedAt == DateTime.MinValue
                    ? "—"
                    : wl.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch { /* 损坏文件：忽略 */ }
        }

        PostToJs(Json(new
        {
            type = "init",
            online,
            // 生效值（已叠加本端覆盖）
            targets = m.Targets.Select(t => new { t.ExeName, t.ExePath }),
            alertPopup = m.AlertPopup,
            alertSound = m.AlertSound,
            alertHighlight = m.AlertHighlight,
            cooldownSeconds = m.CooldownSeconds,
            logRetentionDays = m.LogRetentionDays,
            // 本端是否已覆盖
            managed = _settings.Deployment != null,
            libStatus = online ? "已加载" : "未找到词库文件",
            words,
            libPath,
            updatedAt,
        }));
    }
}
