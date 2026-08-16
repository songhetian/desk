using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WordGuard.Client.App;

/// <summary>
/// 悬浮球状态指示灯（与 prototype/index.html 的 .orb 视觉一致）：
/// 58×58 圆形，径向渐变 (#8fb8ff 中心高光 → #2f6bff 边缘)，柔和蓝色投影，白底护盾 SVG 图标，
/// 常态呼吸，告警态红色脉冲环（向外扩散 26px 渐隐），离线态灰黄无呼吸。
///
/// 使用 Win32 分层窗口（UpdateLayeredWindow + 32bpp ARGB）实现真正的每像素 Alpha 透明。
/// 交互：右键菜单 / 双击状态面板 / 可拖拽 / 关闭=退出。
/// </summary>
public sealed class OrbForm : Form
{
    private readonly OrbStateController _orb;
    private readonly System.Windows.Forms.Timer _timer;
    private int _phase;
    private ContextMenuStrip? _orbMenu;

    // 回调（由 Program.cs 注入）
    public new Action? OnDoubleClick { get; set; }
    public Action? OnExit { get; set; }

    // ---- Win32 分层窗口 ----
    private const int WS_EX_LAYERED = 0x00080000;
    private const int ULW_ALPHA = 0x02;
    private const int AC_SRC_ALPHA = 0x01;

    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private Bitmap? _bufferBmp;
    private IntPtr _bufferHdc = IntPtr.Zero;
    private IntPtr _oldBufferObj = IntPtr.Zero;

    // 配色（对齐 prototype .orb）
    private static readonly Color AccentCenter = Color.FromArgb(143, 184, 255); // #8fb8ff
    private static readonly Color AccentEdge = Color.FromArgb(47, 107, 255);    // #2f6bff
    private static readonly Color AlertCenter = Color.FromArgb(255, 154, 154);   // #ff9a9a
    private static readonly Color AlertEdge = Color.FromArgb(239, 68, 68);      // #ef4444
    private static readonly Color OfflineCenter = Color.FromArgb(233, 220, 151);// #e9dc97
    private static readonly Color OfflineEdge = Color.FromArgb(183, 162, 63);   // #b7a23f
    private static readonly Color ShieldFill = Color.White;

    public OrbForm(OrbStateController orb)
    {
        _orb = orb;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "";

        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Magenta;
        Size = new Size(58, 58); // 与原型 .orb 一致
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        // 右下角悬浮（与原型 position: fixed; right/bottom 一致）
        Location = new Point(screen.Right - 58 - 18, screen.Bottom - 58 - 18);

        _timer = new System.Windows.Forms.Timer { Interval = 50 };
        _timer.Tick += (_, _) => { _phase = (_phase + 1) % 1000; Invalidate(); };
        _timer.Start();
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
        ContextMenuStrip = _orbMenu;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bmp = _bufferBmp ??= new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        DrawOrb(g);
        UpdateLayered(bmp);
    }

    private void DrawOrb(Graphics g)
    {
        var state = _orb.CurrentState(DateTime.UtcNow);
        var (c, e) = state switch
        {
            OrbState.Alert => (AlertCenter, AlertEdge),
            OrbState.Offline => (OfflineCenter, OfflineEdge),
            _ => (AccentCenter, AccentEdge),
        };

        // 呼吸系数（对齐 prototype .orb keyframes breathe 2.6s）
        var breathe = state switch
        {
            OrbState.Normal => 0.55 + 0.45 * Math.Sin(_phase / 14.0),
            OrbState.Alert => 0.4 + 0.6 * Math.Sin(_phase / 4.0),
            _ => 0.35,
        };

        // ---- 投影：模拟 CSS box-shadow 0 6px 22px rgba(47,107,255,.55) ----
        // WinForms 没有真正的 box-shadow；用多层半透明椭圆叠加实现柔和投影
        int shadowLayers = 6;
        for (int i = shadowLayers; i >= 1; i--)
        {
            var offset = i * 1.0f;
            var alpha = (int)(40 * (1.0 - i / (double)shadowLayers));
            using var sb = new SolidBrush(Color.FromArgb(alpha, 47, 107, 255));
            g.FillEllipse(sb, offset * 0.3f, offset, Width + offset * 0.6f, Height + offset);
        }

        // ---- 告警脉冲扩散环（对齐 prototype @keyframes alertpulse 1s，box-shadow 扩散 26px） ----
        if (state == OrbState.Alert)
        {
            var ringT = (_phase % 20) / 20.0; // 1s 周期（_timer 50ms × 20 = 1000ms）
            var expand = (float)(ringT * 26); // 0→26px
            var ringAlpha = (int)(180 * (1 - ringT));
            using var ringPen = new Pen(Color.FromArgb(Math.Max(0, ringAlpha), 239, 68, 68), 2f);
            g.DrawEllipse(ringPen,
                -expand, -expand,
                Width + expand * 2, Height + expand * 2);
        }

        // ---- 主体球体：径向渐变 35% 30%（与 prototype 一致）----
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, Width, Height);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = c,
            SurroundColors = new[] { e },
            // PathGradientBrush 中心点默认为矩形中心；调整到 35% 30%
        };
        brush.CenterPoint = new PointF(Width * 0.35f, Height * 0.30f);
        g.FillEllipse(brush, 0, 0, Width, Height);

        // ---- 呼吸强度叠加外发光（让 Normal 看起来在「呼吸」）----
        if (state == OrbState.Normal)
        {
            var glowAlpha = (int)(90 * breathe);
            using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, 47, 107, 255));
            g.FillEllipse(glowBrush, -3, -3, Width + 6, Height + 6);
        }

        // ---- 白底护盾 SVG 图标（与 prototype .orb 内的 SVG 一致）----
        DrawShieldIcon(g);
    }

    /// <summary>
    /// 居中绘制护盾图标（对齐 prototype .orb 内的 SVG：
    /// &lt;svg viewBox="0 0 24 24"&gt;&lt;path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/&gt;
    /// &lt;line x1="12" y1="8" x2="12" y2="12"/&gt;&lt;line x1="12" y1="16" x2="12.01" y2="16"/&gt;&lt;/svg&gt;）。
    /// </summary>
    private void DrawShieldIcon(Graphics g)
    {
        const float vbSize = 24f;
        // 缩放到 26×26，居中
        var iconSize = 26f;
        var sx = (Width - iconSize) / 2f;
        var sy = (Height - iconSize) / 2f;
        var scale = iconSize / vbSize;

        // 护盾外形
        var shield = new GraphicsPath();
        // 把 SVG path 拆成贝塞尔/直线：M12 22s8-4 8-10 V5 l-8-3 -8 3 v7 c0 6 8 10 8 10z
        // 简化：用 5 段直线连接（M12 22 → 8-10 → 5-3 → -8 3 → -7c0 6 8 10 8 10 → 回到起点）
        shield.AddLines(new[]
        {
            new PointF(sx + 12f * scale, sy + 22f * scale), // bottom
            new PointF(sx + 20f * scale, sy + 12f * scale), // right-bottom
            new PointF(sx + 20f * scale, sy + 5f * scale),  // right-top
            new PointF(sx + 12f * scale, sy + 2f * scale),  // top
            new PointF(sx + 4f * scale, sy + 5f * scale),   // left-top
            new PointF(sx + 4f * scale, sy + 12f * scale),  // left-bottom
        });
        shield.CloseFigure();

        using var shieldBrush = new SolidBrush(ShieldFill);
        g.FillPath(shieldBrush, shield);

        // 内部两条短竖线（感叹号上下段）
        using var dotPen = new Pen(Color.FromArgb(60, 110, 230), 1.8f);
        g.DrawLine(dotPen,
            new PointF(sx + 12f * scale, sy + 8.5f * scale),
            new PointF(sx + 12f * scale, sy + 12f * scale));
        g.DrawLine(dotPen,
            new PointF(sx + 12f * scale, sy + 16.0f * scale),
            new PointF(sx + 12f * scale, sy + 16.05f * scale));
    }

    private void UpdateLayered(Bitmap bmp)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;

        if (_bufferHdc == IntPtr.Zero)
            _bufferHdc = CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.Transparent);
        _oldBufferObj = SelectObject(_bufferHdc, hBitmap);

        var ptDst = new Point(Left, Top);
        var sz = new Size(bmp.Width, bmp.Height);
        var ptSrc = Point.Empty;
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA,
        };

        UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref sz, _bufferHdc, ref ptSrc, 0, ref blend, ULW_ALPHA);

        SelectObject(_bufferHdc, _oldBufferObj);
        DeleteObject(hBitmap);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    // ---- 鼠标交互 ----
    private bool _dragging;
    private Point _dragStart;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Left += e.X - _dragStart.X;
            Top += e.Y - _dragStart.Y;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            OnDoubleClick?.Invoke();
        base.OnMouseDoubleClick(e);
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
        _timer.Dispose();
        _orbMenu?.Dispose();
        _bufferBmp?.Dispose();
        if (_bufferHdc != IntPtr.Zero)
        {
            SelectObject(_bufferHdc, IntPtr.Zero);
            DeleteDC(_bufferHdc);
        }
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* 空实现 */ }
}