using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控日志检索窗口（WebView2 渲染原型 HTML）：按时间范围 + 内容关键字检索审计日志。
/// 触发内容/命中词纯本地存储（可能含客户 PII），仅本机查看。
/// </summary>
public sealed class LogViewerForm : HtmlWindow
{
    private readonly AuditLogStore _store;

    public LogViewerForm(AuditLogStore store) : base("logs.html")
    {
        _store = store;
        Text = "监控日志 — WordGuard";
        Size = new Size(920, 560);
        MinimumSize = new Size(760, 460);
    }

    protected override void OnJsMessage(string json)
    {
        if (json.Contains("\"ready\""))
        {
            PostToJs(Json(new
            {
                type = "init",
                from = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd"),
                to = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            }));
            return;
        }
        if (!json.Contains("\"query\"")) return;

        var from = DateTime.Today.AddDays(-7);
        var to = DateTime.Today.AddDays(1);
        var filter = (string?)null;

        // 轻量解析 query 参数（JSON 来自页面）
        var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("from", out var f) && DateTime.TryParse(f.GetString(), out var fd)) from = fd;
        if (doc.RootElement.TryGetProperty("to", out var t) && DateTime.TryParse(t.GetString(), out var td)) to = td;
        if (doc.RootElement.TryGetProperty("filter", out var fl)) filter = string.IsNullOrWhiteSpace(fl.GetString()) ? null : fl.GetString();
        doc.Dispose();

        var rows = _store.Query(from.Date, to.Date.AddDays(1).AddTicks(-1), filter)
            .Select(e => new
            {
                time = e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                target = e.TargetSoftware,
                window = e.WindowTitle,
                sev = Sev(e.Severity),
                content = e.TriggeredContent,
                words = e.MatchedWords.Select(w => w.Text).ToArray(),
                disp = e.Disposition,
            }).ToArray();

        PostToJs(Json(new { type = "rows", rows }));
    }

    private static string Sev(Severity s) => s switch { Severity.High => "high", Severity.Medium => "medium", _ => "low" };
}
