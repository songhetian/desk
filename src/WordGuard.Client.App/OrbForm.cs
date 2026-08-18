using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WordGuard.Client;

namespace WordGuard.Client.App;

/// <summary>
/// 悬浮球（Premium 风格，纯 GDI 绘制）。
/// 圆形玻璃质感 + 柔光阴影 + W 字母图标。
/// 三态：靛蓝（监控中）/ 红色脉冲（告警）/ 琥珀（离线）。
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
    private bool _hovering;

    public Action? OnOrbDoubleClick { get; set; }
    public Action? OnOrbClick { get; set; }
    public Action? OnExit { get; set; }
    public Action? OnShowSettings { get; set; }
    public Action? OnShowLog { get; set; }
    public Action? OnSimulate { get; set; }

    public Func<int>? GetUnacknowledgedCount { get; set; }

    private ContextMenuStrip? _orbMenu;
    private int _lastBadgeCount = -1;

    // 拖拽状态
    private bool _isDragging;
    private Point _dragStart;
    private Point _formStart;
    private bool _leftMouseDown;
    private const int DragThreshold = 4;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private const int SIZE = 64;

    // Premium 色板
    private static readonly Color PrimaryLight = Color.FromArgb(129, 140, 248);
    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryDark = Color.FromArgb(67, 56, 202);
    private static readonly Color PrimaryDeeper = Color.FromArgb(55, 48, 163);

    private static readonly Color AlertLight = Color.FromArgb(252, 165, 165);
    private static readonly Color Alert = Color.FromArgb(239, 68, 68);
    private static readonly Color AlertDark = Color.FromArgb(185, 28, 28);

    private static readonly Color AmberLight = Color.FromArgb(251, 191, 36);
    private static readonly Color Amber = Color.FromArgb(217, 119, 6);
    private static readonly Color AmberDark = Color.FromArgb(146, 64, 14);

    public OrbForm(OrbStateController orb)
    {
        _orb = orb;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "";
        DoubleBuffered = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        Size = new Size(SIZE + 12, SIZE + 12); // 留边给阴影
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - SIZE - 28, screen.Bottom - SIZE - 28);

        _stateTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _stateTimer.Tick += (_, _) => PushState();

        _pulseTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _pulseTimer.Tick += (_, _) =>
        {
            if (_lastPushed == OrbState.Alert)
            {
                _pulsePhase += 0.1f;
                if (_pulsePhase > 6.28f) _pulsePhase = 0;
                Invalidate();
            }
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
        var hRgn = CreateEllipticRgn(0, 0, SIZE + 12, SIZE + 12);
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
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovering = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovering = false;
        Invalidate();
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
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        g.Clear(Color.Magenta);

        if (!_flashVisible) return;

        var offsetX = 6;
        var offsetY = 6;
        var cx = offsetX + SIZE / 2;
        var cy = offsetY + SIZE / 2;
        var r = SIZE / 2f;

        (Color cTop, Color cMid, Color cBottom, Color cBorder) = _lastPushed switch
        {
            OrbState.Alert => (AlertLight, Alert, AlertDark, Color.FromArgb(153, 27, 27)),
            OrbState.Offline => (AmberLight, Amber, AmberDark, Color.FromArgb(120, 53, 15)),
            _ => (PrimaryLight, Primary, PrimaryDark, PrimaryDeeper),
        };

        var scale = _hovering ? 1.05f : 1f;
        var drawR = r * scale;
        var drawX = cx - drawR;
        var drawY = cy - drawR;
        var drawSize = drawR * 2;

        // ---- 柔光阴影 ----
        for (int i = 4; i >= 0; i--)
        {
            var shadowR = drawR + 3 + i * 1.5f;
            var alpha = 20 - i * 4;
            using var shadowPen = new Pen(Color.FromArgb(alpha, 0, 0, 0), 3);
            g.DrawEllipse(shadowPen, cx - shadowR, cy - shadowR + 1 + i, shadowR * 2, shadowR * 2);
        }

        // ---- 告警脉冲环 ----
        if (_lastPushed == OrbState.Alert)
        {
            var pulse = (float)(0.5 + 0.5 * Math.Sin(_pulsePhase));
            var pulseR = drawR + 6 + pulse * 8;
            var pulseAlpha = (int)(40 + pulse * 60);
            using var pulsePen = new Pen(Color.FromArgb(pulseAlpha, Alert.R, Alert.G, Alert.B), 2.5f);
            g.DrawEllipse(pulsePen, cx - pulseR, cy - pulseR, pulseR * 2, pulseR * 2);
        }

        // ---- 玻璃质感球体（多层渐变）----
        var bodyRect = new RectangleF(drawX, drawY, drawSize, drawSize);

        // 底色（径向渐变：左下深 → 右上浅）
        using var path = new GraphicsPath();
        path.AddEllipse(bodyRect);
        using var bodyBrush = new PathGradientBrush(path)
        {
            CenterColor = cTop,
            SurroundColors = new[] { cBottom },
            CenterPoint = new PointF(cx - drawR * 0.25f, cy - drawR * 0.3f),
        };
        g.FillEllipse(bodyBrush, bodyRect);

        // 中部加强色
        var midRect = new RectangleF(drawX + 2, drawY + drawR * 0.2f, drawSize - 4, drawR * 0.9f);
        using var midPath = new GraphicsPath();
        midPath.AddEllipse(midRect);
        using var midBrush = new PathGradientBrush(midPath)
        {
            CenterColor = Color.FromArgb(80, 255, 255, 255),
            SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
        };
        g.FillEllipse(midBrush, midRect);

        // 边框
        using var borderPen = new Pen(cBorder, 1.5f);
        g.DrawEllipse(borderPen, drawX + 0.5f, drawY + 0.5f, drawSize - 1, drawSize - 1);

        // 内描边（高光边）
        using var innerPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
        g.DrawEllipse(innerPen, drawX + 2.5f, drawY + 2.5f, drawSize - 5, drawSize - 5);

        // ---- 顶部大高光 ----
        var hlW = drawR * 0.7f;
        var hlH = drawR * 0.45f;
        var hlX = cx - hlW * 0.6f;
        var hlY = drawY + drawR * 0.15f;
        var hlRect = new RectangleF(hlX, hlY, hlW, hlH);
        using var hlPath = new GraphicsPath();
        hlPath.AddEllipse(hlRect);
        using var hlBrush = new PathGradientBrush(hlPath)
        {
            CenterColor = Color.FromArgb(220, 255, 255, 255),
            SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
        };
        g.FillEllipse(hlBrush, hlRect);

        // ---- 底部内阴影 ----
        var bsW = drawR * 1.1f;
        var bsH = drawR * 0.5f;
        var bsX = cx - bsW * 0.5f;
        var bsY = cy + drawR * 0.1f;
        var bsRect = new RectangleF(bsX, bsY, bsW, bsH);
        using var bsPath = new GraphicsPath();
        bsPath.AddEllipse(bsRect);
        using var bsBrush = new PathGradientBrush(bsPath)
        {
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(50, 0, 0, 0) },
        };
        g.FillEllipse(bsBrush, bsRect);

        // ---- 中心 W 字母图标 ----
        var iconText = "W";
        var iconFont = new Font("Segoe UI", 18f, FontStyle.Bold);
        var iconSize = g.MeasureString(iconText, iconFont);
        using var iconBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        var iconX = cx - iconSize.Width / 2 + 1;
        var iconY = cy - iconSize.Height / 2 + 2;
        g.DrawString(iconText, iconFont, iconBrush, iconX, iconY);

        // 图标阴影（提升立体感）
        using var iconShadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.DrawString(iconText, iconFont, iconShadowBrush, iconX + 1, iconY + 1);

        // ---- 右上角 Badge ----
        var badgeCount = GetUnacknowledgedCount?.Invoke() ?? 0;
        if (badgeCount > 0)
        {
            var badgeText = badgeCount > 99 ? "99+" : badgeCount.ToString();
            var badgeFont = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Bold);
            var textSize = g.MeasureString(badgeText, badgeFont);

            var badgeW = Math.Max(16, (int)textSize.Width + 8);
            var badgeH = 16;
            var badgeX = (int)(cx + drawR - badgeW * 0.4f);
            var badgeY = (int)(drawY - 2);

            // badge 背景
            using var badgeBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
            g.FillEllipse(badgeBrush, badgeX, badgeY, badgeW, badgeH);

            // badge 边框（白色）
            using var badgePen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(badgePen, badgeX + 0.5f, badgeY + 0.5f, badgeW - 1, badgeH - 1);

            // badge 文字
            using var textBrush = new SolidBrush(Color.White);
            var textX = badgeX + (badgeW - textSize.Width) / 2f;
            var textY = badgeY + (badgeH - textSize.Height) / 2f - 0.5f;
            g.DrawString(badgeText, badgeFont, textBrush, textX, textY);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            OnExit?.Invoke();
        base.OnFormClosing(e);
    }

    public void FlashAlert()
    {
        if (IsDisposed) return;
        _flashCount = 6;
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
