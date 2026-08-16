using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WordGuard.Client.App;

/// <summary>
/// 系统托盘控制器：右键菜单（设置 / 日志 / 模拟 / 退出）。
/// 纯中文标签，不含管理端入口（Studio 是独立软件，不装在员工机器上）。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _appIcon;
    private readonly Bitmap? _fallbackBitmap;

    public TrayController(Action showSettings, Action showLog, Action simulate, Action exit)
    {
        // 优先用 exe 自身图标（ApplicationIcon 已编译进程序），提取失败才回退到 GDI 手绘盾牌
        _appIcon = TryLoadAppIcon();
        _fallbackBitmap = _appIcon is null ? MakeBitmap() : null;
        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = _appIcon ?? Icon.FromHandle(_fallbackBitmap!.GetHicon()),
            Text = "WordGuard 客服违禁词监控",
        };

        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            RenderMode = ToolStripRenderMode.System,
        };

        menu.Items.Add("设置", null, (_, _) => showSettings());
        menu.Items.Add("监控日志", null, (_, _) => showLog());
        menu.Items.Add("模拟告警测试", null, (_, _) => simulate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 WordGuard", null, (_, _) => exit());

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => showSettings();
    }

    private static Icon? TryLoadAppIcon()
    {
        try
        {
            var exe = System.Windows.Forms.Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* 提取失败走 GDI 回退 */ }
        return null;
    }

    private static Bitmap MakeBitmap()
    {
        var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // 盾牌形状（蓝底 + 白色 W 字母）
        using var brush = new SolidBrush(Color.FromArgb(50, 110, 230));
        var path = new GraphicsPath();
        path.AddLines(
        [
            new PointF(16f, 2f),
            new PointF(28f, 8f), new PointF(28f, 18f),
            new PointF(16f, 30f),
            new PointF(4f, 18f), new PointF(4f, 8f),
        ]);
        path.CloseFigure();
        g.FillPath(brush, path);

        // 内部 W 字母
        using var wFont = new Font("Arial", 10f, FontStyle.Bold);
        using var wBrush = new SolidBrush(Color.White);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("W", wFont, wBrush, new RectangleF(6, 8, 20, 18), sf);

        return bmp;
    }

    public void Dispose()
    {
        if (_icon.ContextMenuStrip is { } m) m.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon?.Dispose();
        _fallbackBitmap?.Dispose();
    }

    /// <summary>更新托盘悬浮提示（如「监控中 | 目标: [...]」），让后台运行状态一眼可见。</summary>
    public void SetStatus(string status)
    {
        if (!string.IsNullOrEmpty(status))
            _icon.Text = status.Length > 63 ? status[..63] : status;
    }
}
