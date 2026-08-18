using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 告警弹窗（蓝色 Win11 风格，紧凑精致）。
/// 右下角浮出，8s 未操作自动消失。
/// </summary>
public sealed class AlertPopupForm : Form
{
    public event Action? Confirmed;
    public event Action? Ignored;
    public event Action? DetailsRequested;
    public event Action? TimedOut;

    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 3_000 };
    private readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1_000 };
    private int _secondsLeft = 3;
    private readonly AlertEvent _evt;
    private readonly string _content;
    private readonly string _target;
    private readonly string _windowTitle;
    private readonly string _category;
    private bool _resolved;

    private static readonly Color Primary = Color.FromArgb(59, 130, 246);
    private static readonly Color PrimaryDark = Color.FromArgb(37, 99, 235);
    private static readonly Color BorderGray = Color.FromArgb(229, 231, 235);
    private static readonly Color BgGray = Color.FromArgb(249, 250, 251);

    public AlertPopupForm(AlertEvent evt, string content, string target, string windowTitle, string category)
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
        BackColor = Color.White;
        Size = new Size(380, 300);
        DoubleBuffered = true;

        var area = Screen.GetWorkingArea(Point.Empty);
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);

        BuildUi();

        _timeout.Tick += (_, _) => { _timeout.Stop(); _countdown.Stop(); Resolve("timeout"); };
        _countdown.Tick += (_, _) =>
        {
            _secondsLeft--;
            UpdateCountdown();
            if (_secondsLeft <= 0) _countdown.Stop();
        };
        _timeout.Start();
        _countdown.Start();
    }

    /// <summary>
    /// 弹窗显示时不抢走输入框焦点（用户可以继续打字）。
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;

    /// <summary>
    /// 拦截鼠标激活消息，返回 MA_NOACTIVATE 避免点击弹窗按钮时抢走焦点，
    /// 同时保证按钮 Click 事件能正常触发（ShowWithoutActivation 模式下首次点击会被吞）。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 圆角矩形背景 + 蓝色左边框
        using var path = RoundedRect(0, 0, Width - 1, Height - 1, 10);
        using var pen = new Pen(BorderGray, 1);
        g.DrawPath(pen, path);

        // 左侧蓝色装饰条（Win11 风格）
        using var barBrush = new SolidBrush(Primary);
        g.FillRoundedRectangle(barBrush, new Rectangle(0, 0, 4, Height), 2);
    }

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Label _lblCountdown = null!;

    private void BuildUi()
    {
        // 顶部：标题 + 关闭
        var titleLabel = new Label
        {
            Text = "⚠ 检测到违禁词",
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            Left = 20,
            Top = 14,
            AutoSize = true,
        };
        var closeBtn = new Button
        {
            Text = "✕",
            Font = new Font("Microsoft YaHei UI", 10f),
            ForeColor = Color.FromArgb(156, 163, 175),
            Left = Width - 36,
            Top = 10,
            Size = new Size(24, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(249, 250, 251);
        closeBtn.Click += (_, _) => Resolve("ack");

        // 命中词区域（蓝色背景卡片）
        var words = string.Join("、", _evt.AlertWords.Take(3));
        var wordPanel = new Panel
        {
            Left = 20,
            Top = 46,
            Width = Width - 40,
            Height = 40,
            BackColor = Color.FromArgb(239, 246, 255),
        };
        var wordIcon = new Label
        {
            Text = "🚫",
            Left = 10,
            Top = 8,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13f),
        };
        var wordLabel = new Label
        {
            Text = words,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 64, 175),
            Left = 36,
            Top = 10,
            AutoSize = true,
        };
        wordPanel.Controls.AddRange(new Control[] { wordIcon, wordLabel });

        // 来源 + 分类标签（同一行）
        var infoLine = new Label
        {
            Text = $"来源：{_target}",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(75, 85, 99),
            Left = 20,
            Top = 96,
            AutoSize = true,
        };
        var catLabel = new Label
        {
            Text = _category,
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
            ForeColor = Primary,
            BackColor = Color.FromArgb(219, 234, 254),
            Padding = new Padding(8, 2, 8, 2),
            AutoSize = true,
        };
        using (var g = CreateGraphics())
        {
            var catSize = g.MeasureString(_category, catLabel.Font);
            catLabel.Left = infoLine.Right + 10;
            catLabel.Top = 94;
        }

        // 内容预览标题
        var contentTitle = new Label
        {
            Text = "触发内容",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 65, 81),
            Left = 20,
            Top = 122,
            AutoSize = true,
        };

        // 内容预览框
        var contentBox = new TextBox
        {
            Text = _content,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Left = 20,
            Top = 142,
            Size = new Size(Width - 40, 70),
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = BgGray,
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor = Color.FromArgb(31, 41, 55),
        };

        // 按钮区
        var btnIgnore = new Button
        {
            Text = "忽略",
            Size = new Size(72, 30),
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(75, 85, 99),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnIgnore.FlatAppearance.BorderColor = BorderGray;
        btnIgnore.FlatAppearance.MouseOverBackColor = BgGray;
        btnIgnore.Click += (_, _) => Resolve("ignore");

        var btnDetails = new Button
        {
            Text = "详情",
            Size = new Size(72, 30),
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
            ForeColor = Primary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnDetails.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
        btnDetails.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 246, 255);
        btnDetails.Click += (_, _) => { _timeout.Stop(); _countdown.Stop(); DetailsRequested?.Invoke(); };

        var btnAck = new Button
        {
            Text = "已知悉",
            Size = new Size(80, 30),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            BackColor = Primary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnAck.FlatAppearance.BorderSize = 0;
        btnAck.MouseEnter += (_, _) => btnAck.BackColor = PrimaryDark;
        btnAck.MouseLeave += (_, _) => btnAck.BackColor = Primary;
        btnAck.Click += (_, _) => Resolve("ack");

        var btnPanel = new Panel
        {
            Left = 0,
            Top = 232,
            Width = Width,
            Height = 48,
            BackColor = Color.White,
        };
        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            btnAck.Location = new Point(w - 28, 9);
            btnAck.Left = w - btnAck.Width - 20;
            btnDetails.Left = btnAck.Left - btnDetails.Width - 8;
            btnIgnore.Left = btnDetails.Left - btnIgnore.Width - 8;
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnIgnore, btnDetails, btnAck });
        LayoutBtns(null, EventArgs.Empty);

        // 底部倒计时提示
        _lblCountdown = new Label
        {
            Text = "3 秒后自动关闭",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(156, 163, 175),
            Left = 20,
            Top = 276,
            AutoSize = true,
        };

        Controls.AddRange(new Control[]
        {
            titleLabel, closeBtn,
            wordPanel,
            infoLine, catLabel,
            contentTitle, contentBox,
            btnPanel,
            _lblCountdown,
        });

        AcceptButton = btnAck;
        CancelButton = closeBtn;
    }

    private void UpdateCountdown()
    {
        if (_lblCountdown is not null)
            _lblCountdown.Text = $"{_secondsLeft} 秒后自动关闭";
    }

    private void Resolve(string action)
    {
        if (_resolved) return;
        _resolved = true;
        _timeout.Stop();
        _countdown.Stop();

        switch (action)
        {
            case "ack": Confirmed?.Invoke(); break;
            case "ignore": Ignored?.Invoke(); break;
            case "timeout": TimedOut?.Invoke(); break;
        }
        Close();
    }
}
