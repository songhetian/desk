using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 告警弹窗（现代化）：WebView2 渲染 <c>web/alert-popup.html</c>，盾牌徽标 + 命中词汇卡 + 来源/分类 + 触发内容高亮，
/// 底部「忽略本次 / 查看详情 / 已知悉」。非阻塞、置顶、右下角浮出；60s 未确认触发 <see cref="TimedOut"/>（记「未确认（超时）」）。
///
/// <para>三个按钮语义（由捕获宿主统一写审计与去重确认）：</para>
/// <list type="bullet">
///   <item>已知悉 → <see cref="Confirmed"/>（记「客服已确认」+ 确认去重）；</item>
///   <item>忽略本次 → <see cref="Ignored"/>（记「已忽略」）；</item>
///   <item>查看详情 → <see cref="DetailsRequested"/>（打开审计日志查看器，记「已查看」）。</item>
/// </list>
/// </summary>
public sealed class AlertPopupForm : HtmlWindow
{
    /// <summary>用户点击「已知悉」。</summary>
    public event Action? Confirmed;

    /// <summary>用户点击「忽略本次」或 Esc。</summary>
    public event Action? Ignored;

    /// <summary>用户点击「查看详情」。</summary>
    public event Action? DetailsRequested;

    /// <summary>60 秒超时未确认。</summary>
    public event Action? TimedOut;

    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 60_000 };
    private readonly AlertEvent _evt;
    private readonly string _content;
    private readonly string _target;
    private readonly string _windowTitle;
    private readonly string _category;
    private bool _resolved;

    public AlertPopupForm(AlertEvent evt, string content, string target, string windowTitle, string category)
        : base("alert-popup.html")
    {
        _evt = evt;
        _content = content;
        _target = target;
        _windowTitle = windowTitle;
        _category = category;

        Text = "违禁词提醒";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ShowIcon = false;
        BackColor = Color.Magenta;      // 分层窗口（WebView2 透明背景的前提）
        TransparencyKey = Color.Magenta;
        Size = new Size(404, 352);

        var area = Screen.GetWorkingArea(Point.Empty);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

        _timeout.Tick += (_, _) => { _timeout.Stop(); Resolve("timeout"); };
        _timeout.Start();
    }

    protected override bool TransparentBackground => true;

    /// <summary>弹窗是次要 UI：WebView2 初始化失败时静默关闭即可（审计已记录命中），不弹系统错误框。</summary>
    protected override void OnWebView2Failed(string message) => Resolve("timeout");

    protected override void OnJsMessage(string json)
    {
        string? type = null, action = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (root.TryGetProperty("action", out var a)) action = a.GetString();
        }
        catch { /* 非 JSON：忽略 */ }

        switch (type)
        {
            case "ready": PushInit(); break;
            case "popupAction":
                Resolve(action);
                break;
        }
    }

    private void PushInit()
    {
        PostToJs(Json(new
        {
            type = "init",
            severity = _evt.TopSeverity switch
            {
                Severity.High => "hi",
                Severity.Medium => "mid",
                _ => "lo",
            },
            word = _evt.AlertWords.FirstOrDefault() ?? "",
            words = _evt.AlertWords,
            target = _target,
            windowTitle = _windowTitle,
            category = _category,
            content = _content,
        }));
    }

    /// <summary>把弹窗结局统一收口：停止计时器、只触发一次对应事件并关闭（超时=未确认）。</summary>
    private void Resolve(string? action)
    {
        if (_resolved) return;
        _resolved = true;
        _timeout.Stop();
        switch (action)
        {
            case "ack": Confirmed?.Invoke(); break;
            case "ignore": Ignored?.Invoke(); break;
            case "details": DetailsRequested?.Invoke(); break;
            default: TimedOut?.Invoke(); break;   // "timeout" 或未知 → 未确认（超时）
        }
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timeout.Dispose();
        base.OnFormClosed(e);
    }
}
