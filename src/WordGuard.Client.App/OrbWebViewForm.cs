using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Client;

namespace WordGuard.Client.App;

/// <summary>
/// 悬浮球状态指示灯。优先用 WebView2 渲染 <c>web/orb.html</c>（像素级对齐设计稿）；
/// 若 WebView2 不可用（初始化失败 / 受限环境），则<b>降级为 GDI 手绘球</b>，保证程序始终能打开、可拖拽、可交互。
///
/// <para>技术要点：</para>
/// <list type="bullet">
///   <item>分层窗口：<see cref="TransparencyKey"/> 使窗体成为 WS_EX_LAYERED，WebView2 透明背景才能正确透出桌面；</item>
///   <item>点击穿透：圆形 <see cref="Region"/>，圆外像素不属于窗口，鼠标直达下层应用；圆内可交互；</item>
///   <item>拖拽/交互：双击→状态面板，右键→上下文菜单，关闭=退出（主窗体）；两套渲染（WebView2 / GDI）共用同一套交互逻辑；</item>
///   <item>三态：蓝色呼吸（监控中）/ 红色脉冲（告警）/ 灰黄（离线）。</item>
/// </list>
/// </summary>
public sealed class OrbWebViewForm : HtmlWindow
{
    private readonly OrbStateController _orb;
    private readonly System.Windows.Forms.Timer _stateTimer;
    private OrbState _lastPushed = (OrbState)(-1);

    // 拖拽状态（用物理像素 Cursor.Position 增量，避免 WebView2 CSS 像素与窗体物理像素在 DPI 缩放下不一致导致"拖不动/乱跳"）
    private bool _dragging;
    private Point _dragOrigin;
    private Point _dragScreen;

    // 回调（由 Program.cs 注入）
    public new Action? OnDoubleClick { get; set; }
    public Action? OnExit { get; set; }
    public Action? OnShowSettings { get; set; }
    public Action? OnShowLog { get; set; }
    public Action? OnSimulate { get; set; }

    private ContextMenuStrip? _orbMenu;

    // WebView2 不可用时的 GDI 降级渲染
    private bool _useFallback;

    public OrbWebViewForm(OrbStateController orb) : base("orb.html")
    {
        _orb = orb;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "";

        // 分层窗口：TransparencyKey 让窗体获得 WS_EX_LAYERED 样式，
        // 这是 WebView2 透明背景能正确透出桌面的前提（只设 BackColor=Transparent 不够，会导致初始化/渲染异常）。
        // 该色仅作为"透明键"，实际画面被 WebView2 完全覆盖，不会裸露出洋红。
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;

        // 略大于悬浮球，给光晕/脉冲留出绘制空间（圆形裁剪后多余部分天然点击穿透）
        const int size = 110;
        Size = new Size(size, size);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - size - 18, screen.Bottom - size - 18);

        // 圆形裁剪：圆外区域不拦截鼠标，圆内（含悬浮球与其光晕/脉冲）可交互
        using var gp = new GraphicsPath();
        gp.AddEllipse(0, 0, size, size);
        Region = new Region(gp);

        _stateTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _stateTimer.Tick += (_, _) => PushState();
    }

    protected override bool TransparentBackground => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _stateTimer.Start();
    }

    /// <summary>WebView2 初始化失败 → 降级为 GDI 手绘球，绝不直接退出主窗体。</summary>
    protected override void OnWebView2Failed(string message)
    {
        _useFallback = true;
        DisableWebView();
        Invalidate();
    }

    /// <summary>由 Program.cs 调用，挂上右键菜单项。</summary>
    public void AttachMenu(params ToolStripItem[] items)
    {
        _orbMenu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            RenderMode = ToolStripRenderMode.System,
        };
        _orbMenu.Items.AddRange(items);
    }

    protected override void OnJsMessage(string json)
    {
        string? type = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch { /* 非 JSON 或字段缺失：忽略 */ }

        switch (type)
        {
            case "ready":
                PushState(force: true);
                break;
            case "dragStart":
                _dragging = true;
                _dragOrigin = Location;
                _dragScreen = Cursor.Position;
                break;
            case "dragMove" when _dragging:
                // 物理像素增量，任何 DPI 下都与窗体 Location 一致
                Left = _dragOrigin.X + (Cursor.Position.X - _dragScreen.X);
                Top = _dragOrigin.Y + (Cursor.Position.Y - _dragScreen.Y);
                break;
            case "dragEnd":
                _dragging = false;
                break;
            case "doubleClick":
                OnDoubleClick?.Invoke();
                break;
            case "rightClick":
                if (_useFallback)
                    _orbMenu?.Show(Cursor.Position);          // GDI 降级：WinForms 菜单
                else
                    ShowModernMenu();                          // WebView2 正常：HTML 现代化菜单
                break;
        }
    }

    /// <summary>弹出现代化右键菜单（独立 WebView2 弹层，不会受悬浮球圆形裁剪/覆盖影响）。</summary>
    private void ShowModernMenu()
    {
        var menu = new OrbMenuForm(action =>
        {
            switch (action)
            {
                case "settings": OnShowSettings?.Invoke(); break;
                case "log": OnShowLog?.Invoke(); break;
                case "simulate": OnSimulate?.Invoke(); break;
                case "exit": OnExit?.Invoke(); break;
            }
        });
        menu.FormClosed += (_, _) => menu.Dispose();
        menu.Show();
        menu.Activate();
    }

    // ---------- GDI 降级交互（与 WebView2 模式共用，保证降级后依然可操作）----------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOrigin = Location;
            _dragScreen = Cursor.Position;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Left = _dragOrigin.X + (Cursor.Position.X - _dragScreen.X);
            Top = _dragOrigin.Y + (Cursor.Position.Y - _dragScreen.Y);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _orbMenu?.Show(Cursor.Position);
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            OnDoubleClick?.Invoke();
        base.OnMouseDoubleClick(e);
    }

    /// <summary>把悬浮球三态推送给 JS（仅在状态变化时）；GDI 降级模式下改为重绘。</summary>
    private void PushState(bool force = false)
    {
        var s = _orb.CurrentState(DateTime.UtcNow);
        if (!force && s == _lastPushed) return;
        _lastPushed = s;
        if (_useFallback)
        {
            Invalidate();
            return;
        }
        var stateName = s switch
        {
            OrbState.Alert => "alert",
            OrbState.Offline => "offline",
            _ => "normal",
        };
        PostToJs(Json(new { type = "state", state = stateName }));
    }

    // ---------- GDI 手绘球（降级渲染）----------
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_useFallback) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;

        const int r = 29;
        var cx = Width / 2;
        var cy = Height / 2;

        (Color c1, Color c2) = _lastPushed switch
        {
            OrbState.Alert => (Color.FromArgb(255, 154, 154), Color.FromArgb(239, 68, 68)),
            OrbState.Offline => (Color.FromArgb(233, 220, 151), Color.FromArgb(183, 162, 63)),
            _ => (Color.FromArgb(143, 184, 255), Color.FromArgb(47, 107, 255)),
        };

        using var path = new GraphicsPath();
        path.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = c1,
            SurroundColors = new[] { c2 },
            CenterPoint = new PointF(cx - r * 0.18f, cy - r * 0.22f),
        };
        g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);

        // 护盾图标（白色描边 + 感叹号）
        DrawShield(g, cx, cy, c2 == Color.FromArgb(239, 68, 68) ? 1 : 0);

        // 告警态外环
        if (_lastPushed == OrbState.Alert)
        {
            using var ring = new Pen(Color.FromArgb(120, 239, 68, 68), 3);
            g.DrawEllipse(ring, cx - r - 4, cy - r - 4, 2 * (r + 4), 2 * (r + 4));
        }
    }

    private static void DrawShield(Graphics g, int cx, int cy, int variant)
    {
        var s = 11;
        using var shield = new GraphicsPath();
        shield.AddLine(cx, cy - s - 2, cx + s, cy - s + 2);
        shield.AddLine(cx + s, cy + 2, cx, cy + s + 2);
        shield.AddLine(cx, cy + s + 2, cx - s, cy + 2);
        shield.AddLine(cx - s, cy - s + 2, cx, cy - s - 2);
        shield.CloseFigure();

        using var pen = new Pen(Color.White, 2.2f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawPath(pen, shield);

        // 感叹号
        using var ex = new Pen(Color.White, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        g.DrawLine(ex, cx, cy - 6, cx, cy + 1);
        using var b = new SolidBrush(Color.White);
        g.FillEllipse(b, cx - 1.2f, cy + 4, 2.4f, 2.4f);
    }

    // 关闭悬浮球 = 退出整个程序
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            OnExit?.Invoke();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _stateTimer.Dispose();
        _orbMenu?.Dispose();
        base.OnFormClosed(e);
    }
}
