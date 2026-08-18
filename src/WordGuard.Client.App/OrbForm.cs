using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WordGuard.Client;

namespace WordGuard.Client.App;

/// <summary>
/// 悬浮球（纯 GDI 绘制，圆形窗口，无锯齿透明边）。
/// 三态：蓝色（监控中）/ 红色脉冲（告警）/ 黄色（离线）。
/// 支持拖拽、单击打开状态面板、双击打开设置、右键菜单。
/// </summary>
public sealed class OrbForm : Form
{
    private readonly OrbStateController _orb;
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly System.Windows.Forms.Timer _pulseTimer;
    private readonly System.Windows.Forms.Timer _flashTimer;
    private OrbState _lastPushed = (OrbState)(-1);
    private float _pulsePhase;
    private int _flashCount;
    private bool _flashVisible = true;

    public Action? OnOrbDoubleClick { get; set; }
    public Action? OnOrbClick { get; set; }
    public Action? OnExit { get; set; }
    public Action? OnShowSettings { get; set; }
    public Action? OnShowLog { get; set; }
    public Action? OnSimulate { get; set; }

    /// <summary>获取未确认告警数（用于右上角 badge 显示）。</summary>
    public Func<int>? GetUnacknowledgedCount { get; set; }

    private ContextMenuStrip? _orbMenu;

    private int _lastBadgeCount = -1;

    // 拖拽状态
    private bool _isDragging;
    private Point _dragStart;
    private Point _formStart;
    private bool _leftMouseDown;
    private const int DragThreshold = 4;

    // Win32
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int rx, int ry);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private const int SIZE = 56;

    public OrbForm(OrbStateController orb)
    {
        _orb = orb;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "";
        DoubleBuffered = true;

        Size = new Size(SIZE, SIZE);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - SIZE - 20, screen.Bottom - SIZE - 20);

        _stateTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _stateTimer.Tick += (_, _) => PushState();

        _pulseTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _pulseTimer.Tick += (_, _) =>
        {
            if (_lastPushed == OrbState.Alert)
            {
                _pulsePhase += 0.12f;
                if (_pulsePhase > 6.28f) _pulsePhase = 0;
                Invalidate();
            }
            // 检查 badge 数量变化
            var count = GetUnacknowledgedCount?.Invoke() ?? 0;
            if (count != _lastBadgeCount)
            {
                _lastBadgeCount = count;
                Invalidate();
            }
        };

        _flashTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _flashTimer.Tick += (_, _) =>
        {
            _flashVisible = !_flashVisible;
            Invalidate();
            _flashCount--;
            if (_flashCount <= 0)
            {
                _flashTimer.Stop();
                _flashVisible = true;
                Invalidate();
            }
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 设置圆形窗口区域（真正的圆形，不是透明色抠图，无锯齿）
        var hRgn = CreateEllipticRgn(0, 0, SIZE, SIZE);
        SetWindowRgn(Handle, hRgn, true);

        _stateTimer.Start();
        _pulseTimer.Start();
    }

    public void AttachMenu(params ToolStripItem[] items)
    {
        _orbMenu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            RenderMode = ToolStripRenderMode.System,
        };
        _orbMenu.Items.AddRange(items);
        // 不用 ContextMenuStrip 属性（圆形区域可能导致右键异常），改用手动触发
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _leftMouseDown = true;
            _isDragging = false;
            _dragStart = Cursor.Position;
            _formStart = Location;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_leftMouseDown && !_isDragging)
        {
            var dx = Cursor.Position.X - _dragStart.X;
            var dy = Cursor.Position.Y - _dragStart.Y;
            if (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)
                _isDragging = true;
        }

        if (_isDragging)
        {
            var dx = Cursor.Position.X - _dragStart.X;
            var dy = Cursor.Position.Y - _dragStart.Y;
            Location = new Point(_formStart.X + dx, _formStart.Y + dy);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (!_isDragging)
                OnOrbClick?.Invoke();
            _leftMouseDown = false;
            _isDragging = false;
        }
        else if (e.Button == MouseButtons.Right && _orbMenu is not null)
        {
            _orbMenu.Show(this, e.Location);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _leftMouseDown = false;
            OnOrbDoubleClick?.Invoke();
        }
        base.OnMouseDoubleClick(e);
    }

    private void PushState(bool force = false)
    {
        var s = _orb.CurrentState(DateTime.UtcNow);
        if (!force && s == _lastPushed) return;
        _lastPushed = s;
        _pulsePhase = 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // 闪烁时不画（快速闪烁效果）
        if (!_flashVisible) return;

        var cx = SIZE / 2;
        var cy = SIZE / 2;
        var r = SIZE / 2 - 1;

        (Color cInner, Color cOuter, Color cBorder) = _lastPushed switch
        {
            OrbState.Alert => (
                Color.FromArgb(254, 202, 202),
                Color.FromArgb(220, 38, 38),
                Color.FromArgb(185, 28, 28)
            ),
            OrbState.Offline => (
                Color.FromArgb(254, 240, 138),
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(180, 83, 9)
            ),
            _ => (
                Color.FromArgb(191, 219, 254),
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(29, 78, 216)
            ),
        };

        // ---- 告警状态：脉冲外圈（呼吸效果）----
        if (_lastPushed == OrbState.Alert)
        {
            var pulse = (float)(0.5 + 0.5 * Math.Sin(_pulsePhase));
            var pulseR = r - 2 - pulse * 3;
            var pulseAlpha = (int)(80 + pulse * 80);
            using var pulsePen = new Pen(Color.FromArgb(pulseAlpha, 255, 255, 255), 2.5f);
            g.DrawEllipse(pulsePen, cx - pulseR, cy - pulseR, 2 * pulseR, 2 * pulseR);
        }

        // ---- 主球体（路径渐变）----
        using var path = new GraphicsPath();
        path.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = cInner,
            SurroundColors = new[] { cOuter },
            CenterPoint = new PointF(cx - r * 0.3f, cy - r * 0.35f),
        };
        g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);

        // ---- 细边框（增加精致感）----
        using var borderPen = new Pen(cBorder, 1.5f);
        g.DrawEllipse(borderPen, cx - r + 0.5f, cy - r + 0.5f, 2 * r - 1, 2 * r - 1);

        // ---- 顶部高光（椭圆光斑）----
        var hlRect = new RectangleF(cx - r * 0.45f, cy - r * 0.7f, r * 0.65f, r * 0.45f);
        using var hlPath = new GraphicsPath();
        hlPath.AddEllipse(hlRect);
        using var hlBrush = new PathGradientBrush(hlPath)
        {
            CenterColor = Color.FromArgb(200, 255, 255, 255),
            SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
        };
        g.FillEllipse(hlBrush, hlRect);

        // ---- 底部内阴影 ----
        var bsRect = new RectangleF(cx - r * 0.7f, cy + r * 0.15f, r * 1.4f, r * 0.6f);
        using var bsPath = new GraphicsPath();
        bsPath.AddEllipse(bsRect);
        using var bsBrush = new PathGradientBrush(bsPath)
        {
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(60, 0, 0, 0) },
        };
        g.FillEllipse(bsBrush, bsRect);

        // ---- 图标（盾牌 + 感叹号）----
        DrawShieldIcon(g, cx, cy, _lastPushed == OrbState.Alert
            ? Color.FromArgb(220, 38, 38)
            : Color.FromArgb(37, 99, 235));

        // ---- 右上角告警计数 badge ----
        var badgeCount = GetUnacknowledgedCount?.Invoke() ?? 0;
        if (badgeCount > 0)
        {
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
            var textSize = g.MeasureString(badgeText, badgeFont);

            // badge 背景尺寸（根据文字宽度自适应）
            var badgeW = Math.Max(18, (int)textSize.Width + 8);
            var badgeH = 18;
            var badgeX = SIZE - badgeW + 2;
            var badgeY = 2;

            // badge 背景（红色圆形/胶囊形）
            using var badgePath = new GraphicsPath();
            badgePath.AddEllipse(badgeX, badgeY, badgeW, badgeH);
            using var badgeBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
            g.FillEllipse(badgeBrush, badgeX, badgeY, badgeW, badgeH);

            // badge 白色边框
            using var badgePen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(badgePen, badgeX + 0.5f, badgeY + 0.5f, badgeW - 1, badgeH - 1);

            // badge 文字
            using var textBrush = new SolidBrush(Color.White);
            var textX = badgeX + (badgeW - textSize.Width) / 2;
            var textY = badgeY + (badgeH - textSize.Height) / 2 - 1;
            g.DrawString(badgeText, badgeFont, textBrush, textX, textY);
        }
    }

    private static void DrawShieldIcon(Graphics g, int cx, int cy, Color exColor)
    {
        var s = 11;
        var y0 = cy + 1;

        // 盾牌路径
        using var shield = new GraphicsPath();
        shield.AddLine(cx, y0 - s - 1, cx + s, y0 - s + 2);
        shield.AddLine(cx + s, y0 + 2, cx, y0 + s + 1);
        shield.AddLine(cx, y0 + s + 1, cx - s, y0 + 2);
        shield.AddLine(cx - s, y0 - s + 2, cx, y0 - s - 1);
        shield.CloseFigure();

        // 盾牌阴影
        using var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));
        var shadowPath = (GraphicsPath)shield.Clone();
        using var mat = new Matrix();
        mat.Translate(0, 1.5f);
        shadowPath.Transform(mat);
        g.FillPath(shadowBrush, shadowPath);

        // 盾牌主体（白色填充）
        using var fillBrush = new SolidBrush(Color.White);
        g.FillPath(fillBrush, shield);

        // 盾牌边框
        using var borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
        g.DrawPath(borderPen, shield);

        // 感叹号
        using var exBrush = new SolidBrush(exColor);
        g.FillRectangle(exBrush, cx - 1.3f, y0 - 6, 2.6f, 7);
        g.FillEllipse(exBrush, cx - 1.4f, y0 + 2.5f, 2.8f, 2.8f);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            OnExit?.Invoke();
        base.OnFormClosing(e);
    }

    /// <summary>触发一次闪烁提醒（用于命中违禁词时）。</summary>
    public void FlashAlert()
    {
        if (IsDisposed) return;
        _flashCount = 6; // 闪烁 3 次（6 次开关切换 = 3 个完整周期）
        _flashVisible = true;
        _flashTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _stateTimer.Dispose();
        _pulseTimer.Dispose();
        _flashTimer.Dispose();
        _orbMenu?.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>GDI+ 圆角矩形扩展方法。</summary>
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
