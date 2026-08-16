using System.IO;
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
            case "renameCategory": RenameCategory(doc); break;
            case "deleteCategory": DeleteCategory(doc); break;
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
            categories = CategoryJson(),
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
        categories = CategoryJson(),
        deploy = DeployJson(),
    }));

    private object CategoryJson() => _editor.GetCategories()
        .Select(c => new { name = c.Name, count = c.Count });

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
        Persist();
        PushUpdated();
    }

    private void DeleteWord(JsonDocument doc)
    {
        var id = doc.RootElement.GetProperty("id").GetGuid();
        _editor.Remove(id);
        Persist();
        PushUpdated();
    }

    private void SaveWord(JsonDocument doc)
    {
        var root = doc.RootElement;
        var text = (root.GetProperty("text").GetString() ?? "").Trim();
        var category = root.GetProperty("category").GetString() ?? "";
        var sev = root.GetProperty("severity").GetString() switch
        {
            "high" => Severity.High,
            "low" => Severity.Low,
            _ => Severity.Medium,
        };
        var enabled = root.GetProperty("enabled").GetBoolean();

        Guid? id = null;
        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(idEl.GetString(), out var parsed) && parsed != Guid.Empty)
            id = parsed;

        // 校验：空文本 / 重复（编辑时排除自身）—— 结果通过 toast 反馈，HTML 不再依赖原生 alert
        if (text.Length == 0)
        {
            PostToJs(Json(new { type = "toast", text = "违禁词文本不能为空", ok = false }));
            return;
        }
        var dup = _lib.Words.Any(w =>
            w.Text.Trim().Equals(text, StringComparison.OrdinalIgnoreCase) && w.Id != id);
        if (dup)
        {
            PostToJs(Json(new { type = "toast", text = $"「{text}」已存在于词库，请勿重复添加", ok = false }));
            return;
        }

        if (id is { } existingId)
        {
            var existing = _lib.Words.FirstOrDefault(w => w.Id == existingId);
            if (existing is null) return;
            _editor.Update(existingId, existing with { Text = text, Category = category, Severity = sev, Enabled = enabled });
        }
        else
        {
            _editor.Add(new WordEntry { Text = text, Category = category, Severity = sev, Enabled = enabled });
        }
        Persist();
        PushUpdated();
        PostToJs(Json(new { type = "toast", text = id is null ? "已新增违禁词并保存 ✓" : "已更新词条并保存 ✓", ok = true }));
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
        Persist();
        PushUpdated();
        PostToJs(Json(new { type = "toast", text = "默认策略已保存并写入词库 ✓", ok = true }));
    }

    private void RenameCategory(JsonDocument doc)
    {
        var root = doc.RootElement;
        var oldName = root.GetProperty("old").GetString() ?? "";
        var newName = root.GetProperty("new").GetString() ?? "";
        var n = _editor.RenameCategory(oldName, newName);
        Persist();
        PushUpdated();
        PostToJs(Json(new { type = "toast", text = n > 0 ? $"已重命名分类「{oldName}」→「{newName}」（{n} 词）" : "分类无变化", ok = n > 0 }));
    }

    private void DeleteCategory(JsonDocument doc)
    {
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString() ?? "";
        var reassignTo = root.TryGetProperty("reassignTo", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "";
        var n = _editor.DeleteCategory(name, reassignTo);
        Persist();
        PushUpdated();
        PostToJs(Json(new { type = "toast", text = n > 0 ? $"已删除分类「{name}」（{n} 词已迁移）" : "分类无变化", ok = n > 0 }));
    }

    /// <summary>
    /// 导出词库：弹「另存为」让管理员选择目标路径（修复旧版"导出=把文件写回自己"无效问题）。
    /// 导出即写盘并刷新 updatedAt，客户端据此判断下发是否生效。
    /// </summary>
    private void Export()
    {
        try
        {
            var json = _editor.Export();
            using var dlg = new SaveFileDialog
            {
                Title = "导出词库（供客户端下发）",
                Filter = "词库 JSON (*.json)|*.json",
                FileName = $"wordlib-{DateTime.Now:yyyyMMdd-HHmm}.json",
                DefaultExt = "json",
                AddExtension = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return; // 用户取消导出
            File.WriteAllText(dlg.FileName, json);
            PostToJs(Json(new
            {
                type = "toast",
                text = $"已导出 {_lib.Words.Count} 词 + {_lib.Metadata.Targets.Count} 目标 → {dlg.FileName}",
                ok = true,
            }));
        }
        catch (Exception ex)
        {
            PostToJs(Json(new { type = "toast", text = $"导出失败：{ex.Message}", ok = false }));
        }
    }

    /// <summary>把内存词库（词条 + 默认策略 metadata）写回词库文件。旧版缺失此步，所有编辑"无反应"（重启即丢）。</summary>
    private void Persist()
    {
        _lib.UpdatedAt = DateTime.UtcNow;
        File.WriteAllText(_path, _lib.ToJson());
    }

    private static string Sev(Severity s) => s switch { Severity.High => "high", Severity.Medium => "medium", _ => "low" };
}
