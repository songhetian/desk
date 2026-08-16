using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace WordGuard.Client.App;

/// <summary>
/// 悬浮球右键菜单弹层：独立 WebView2 窗口渲染 <c>web/orb-menu.html</c>，
/// 实现现代化菜单（品牌头部 / 入场动画 / 悬停态），不依赖 WinForms ContextMenuStrip。
/// 点击菜单项 / 按 Esc / 失去焦点即关闭；若 WebView2 不可用则整体不显示（悬浮球仍可用降级 WinForms 菜单）。
/// </summary>
public sealed class OrbMenuForm : HtmlWindow
{
    private readonly Action<string> _onAction;
    private bool _closed;

    public OrbMenuForm(Action<string> onAction) : base("orb-menu.html")
    {
        _onAction = onAction;
        Text = "";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;      // 分层窗口（WebView2 透明背景的前提）
        TransparencyKey = Color.Magenta;
        ShowIcon = false;
        Size = new Size(246, 258);

        // 定位：菜单左上角贴光标，超出工作区时收回到屏内
        var p = Cursor.Position;
        var area = Screen.FromPoint(p).WorkingArea;
        var x = Math.Min(p.X, area.Right - Width);
        var y = Math.Min(p.Y, area.Bottom - Height);
        Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));

        // 失去焦点（点击别处 / Alt-Tab）→ 关闭菜单
        Deactivate += (_, _) => Close();
    }

    protected override bool TransparentBackground => true;

    /// <summary>菜单是次要 UI：WebView2 初始化失败时静默关闭即可，不要弹错误框打扰用户。</summary>
    protected override void OnWebView2Failed(string message) => Close();

    protected override void OnJsMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "menu":
                    var action = root.GetProperty("action").GetString() ?? "";
                    Close();   // 先关菜单，再派发动作
                    _onAction(action);
                    break;
                case "close":
                    Close();
                    break;
            }
        }
        catch { /* 非 JSON：忽略 */ }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_closed)
        {
            _closed = true;
            base.OnFormClosed(e);
        }
    }
}
