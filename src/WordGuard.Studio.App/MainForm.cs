using System.Text.Json;
using System.Windows.Forms;
using WordGuard.Core;
using WordGuard.Studio;

namespace WordGuard.Studio.App;

/// <summary>
/// 词库管理端主窗口（WebView2 渲染原型 HTML）。
/// HTML 页面通过消息桥请求 CRUD / 部署配置 / 导出；本类持有 WordLibraryEditor 并回传数据。
/// 视觉与 prototype/index.html 的 Studio 主题一致（紫色渐变标题栏 / 卡片 / 斑马表格）。
/// </summary>
public sealed class MainForm : HtmlWindow
{
    private readonly string _path;
    private readonly WordLibrary _lib;
    private readonly WordLibraryEditor _editor;

    public MainForm(string path) : base("studio.html")
    {
        _path = path;
        _lib = WordLibrary.LoadFromFile(path);
        _editor = new WordLibraryEditor(_lib);

        Text = "词库管理端 — WordGuard Studio";
        Size = new Size(1040, 660);
        MinimumSize = new Size(840, 520);
    }

    protected override void OnJsMessage(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return; }

        var type = doc.RootElement.GetProperty("type").GetString();
        switch (type)
        {
            case "init": PushInit(); break;
            case "toggleWord": ToggleWord(doc); break;
            case "deleteWord": DeleteWord(doc); break;
            case "saveWord": SaveWord(doc); break;
            case "saveDeploy": SaveDeploy(doc); break;
            case "export": Export(); break;
        }
        doc.Dispose();
    }

    private void PushInit()
    {
        PostToJs(Json(new
        {
            type = "init",
            words = _lib.Words.Select(w => new
            {
                w.Id,
                w.Text,
                w.Category,
                Severity = Sev(w.Severity),
                w.Enabled,
            }),
            deploy = DeployJson(),
            libPath = _path,
        }));
    }

    private void PushUpdated() => PostToJs(Json(new
    {
        type = "updated",
        words = _lib.Words.Select(w => new
        {
            w.Id,
            w.Text,
            w.Category,
            Severity = Sev(w.Severity),
            w.Enabled,
        }),
        deploy = DeployJson(),
    }));

    private object DeployJson() => new
    {
        targets = _lib.Metadata.Targets.Select(t => new { t.ExeName, t.ExePath }),
        alertPopup = _lib.Metadata.AlertPopup,
        alertSound = _lib.Metadata.AlertSound,
        alertHighlight = _lib.Metadata.AlertHighlight,
        soundFilePath = _lib.Metadata.SoundFilePath,
        cooldownSeconds = _lib.Metadata.CooldownSeconds,
        logRetentionDays = _lib.Metadata.LogRetentionDays,
    };

    private void ToggleWord(JsonDocument doc)
    {
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var enabled = doc.RootElement.GetProperty("enabled").GetBoolean();
        _editor.SetEnabled(id, enabled);
        PushUpdated();
    }

    private void DeleteWord(JsonDocument doc)
    {
        var id = doc.RootElement.GetProperty("id").GetGuid();
        _editor.Remove(id);
        PushUpdated();
    }

    private void SaveWord(JsonDocument doc)
    {
        var root = doc.RootElement;
        var text = root.GetProperty("text").GetString() ?? "";
        var category = root.GetProperty("category").GetString() ?? "";
        var sev = root.GetProperty("severity").GetString() switch
        {
            "high" => Severity.High,
            "low" => Severity.Low,
            _ => Severity.Medium,
        };
        var enabled = root.GetProperty("enabled").GetBoolean();

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(idEl.GetString(), out var id) && id != Guid.Empty)
        {
            var existing = _lib.Words.FirstOrDefault(w => w.Id == id);
            if (existing is not null)
                _editor.Update(id, existing with { Text = text, Category = category, Severity = sev, Enabled = enabled });
        }
        else
        {
            _editor.Add(new WordEntry { Text = text, Category = category, Severity = sev, Enabled = enabled });
        }
        PushUpdated();
    }

    private void SaveDeploy(JsonDocument doc)
    {
        var root = doc.RootElement;
        var m = _lib.Metadata;
        m.Targets.Clear();
        if (root.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            foreach (var t in targets.EnumerateArray())
                m.Targets.Add(new TargetSpec
                {
                    ExeName = t.GetProperty("exeName").GetString() ?? "",
                    ExePath = t.TryGetProperty("exePath", out var p) && p.ValueKind == JsonValueKind.String
                        ? (string.IsNullOrEmpty(p.GetString()) ? null : p.GetString())
                        : null,
                });
        m.AlertPopup = root.GetProperty("alertPopup").GetBoolean();
        m.AlertSound = root.GetProperty("alertSound").GetBoolean();
        m.AlertHighlight = root.GetProperty("alertHighlight").GetBoolean();
        m.CooldownSeconds = root.GetProperty("cooldownSeconds").GetInt32();
        m.LogRetentionDays = root.GetProperty("logRetentionDays").GetInt32();
        PushUpdated();
    }

    private void Export()
    {
        try
        {
            var json = _editor.Export();
            System.IO.File.WriteAllText(_path, json);
            PostToJs(Json(new { type = "toast", text = $"已导出 {_lib.Words.Count} 词 + {_lib.Metadata.Targets.Count} 目标 ✓", ok = true }));
        }
        catch (System.Exception ex)
        {
            PostToJs(Json(new { type = "toast", text = $"导出失败：{ex.Message}", ok = false }));
        }
    }

    private static string Sev(Severity s) => s switch { Severity.High => "high", Severity.Medium => "medium", _ => "low" };
}
