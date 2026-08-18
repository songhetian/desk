using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 告警弹窗（Premium 靛蓝色风格）。
/// 右下角浮出，倒计时自动消失。
/// </summary>
public sealed class AlertPopupForm : Form
{
    public event Action? Confirmed;
    public event Action? Ignored;
    public event Action? DetailsRequested;
    public event Action? TimedOut;

    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 8_000 };
    private readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1_000 };
    private int _secondsLeft = 8;
    private readonly AlertEvent _evt;
    private readonly string _content;
    private readonly string _target;
    private readonly string _category;
    private bool _resolved;

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color PrimaryLight = Color.FromArgb(238, 240, 255);
    private static readonly Color PrimaryBg = Color.FromArgb(244, 245, 255);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(248, 250, 252);
    private static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    private static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);

    public AlertPopupForm(AlertEvent evt, string content, string target, string windowTitle, string category)
    {
        _evt = evt;
        _content = content;
        _target = target;
        _category = category;

        Text = "违禁词提醒";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ShowIcon = false;
        BackColor = Color.White;
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 9f);

        Size = new Size(420, 300);

        var area = Screen.GetWorkingArea(Point.Empty);
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);

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

    protected override bool ShowWithoutActivation => true;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;

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

        using var path = RoundedRect(0, 0, Width - 1, Height - 1, 12);
        using var pen = new Pen(BorderGray, 1);
        g.DrawPath(pen, path);
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
        const int pad = 20;
        var y = 0;

        // 顶部标题栏（48px）
        var iconBox = new Panel
        {
            Left = pad,
            Top = 12,
            Size = new Size(24, 24),
            BackColor = PrimaryLight,
        };
        var iconLbl = new Label
        {
            Text = "!",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Primary,
        };
        iconBox.Controls.Add(iconLbl);

        var titleLbl = new Label
        {
            Text = "检测到违禁词",
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Left = 52,
            Top = 14,
            AutoSize = true,
        };

        var closeBtn = new Button
        {
            Text = "✕",
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = TextMuted,
            Size = new Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = BgGray;
        closeBtn.Click += (_, _) => Resolve("ack");
        closeBtn.Left = Width - closeBtn.Width - 10;
        closeBtn.Top = 10;

        y = 48;

        // 分割线
        var div1 = new Panel
        {
            Left = 0,
            Top = y,
            Width = Width,
            Height = 1,
            BackColor = BorderGray,
        };
        y += 1;

        // 违禁词卡片（64px）
        var wordPanel = new Panel
        {
            Left = 0,
            Top = y,
            Width = Width,
            Height = 64,
            BackColor = PrimaryBg,
        };
        var wordIcon = new Label
        {
            Text = "⛔",
            Left = pad,
            Top = 18,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 18f),
        };
        var displayWords = string.Join("、", _evt.AlertWords.Take(5));
        var wordLbl = new Label
        {
            Text = displayWords,
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = Primary,
            Left = 56,
            Top = 22,
            AutoSize = true,
        };
        wordPanel.Controls.AddRange(new Control[] { wordIcon, wordLbl });
        y += 64;

        // 信息行（40px）
        var sourceLbl = new Label
        {
            Text = $"来源：{_target}",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = TextSecondary,
            Left = pad,
            Top = y + 12,
            AutoSize = true,
        };

        var catText = string.IsNullOrEmpty(_category) ? "未分类" : _category;
        var catLbl = new Label
        {
            Text = catText,
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
            ForeColor = Primary,
            BackColor = PrimaryLight,
            Padding = new Padding(10, 3, 10, 3),
            AutoSize = true,
        };
        using (var g = CreateGraphics())
        {
            var catSize = g.MeasureString(catText, catLbl.Font);
            catLbl.Left = Width - (int)catSize.Width - pad - 16;
            catLbl.Top = y + 10;
        }
        y += 40;

        // 触发内容标题
        var contentTitle = new Label
        {
            Text = "触发内容",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Left = pad,
            Top = y + 8,
            AutoSize = true,
        };
        y += 30;

        // 内容框（底部预留 64px 给按钮栏）
        var bottomH = 64;
        var contentH = Height - y - bottomH - 12;
        var contentBox = new TextBox
        {
            Text = _content,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = _content.Length > 100 ? ScrollBars.Vertical : ScrollBars.None,
            Left = pad,
            Top = y,
            Size = new Size(Width - pad * 2, contentH),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = BgGray,
            BorderStyle = BorderStyle.None,
            ForeColor = TextPrimary,
        };

        // 底部按钮栏背景
        var bottomPanel = new Panel
        {
            Left = 0,
            Top = Height - bottomH,
            Width = Width,
            Height = bottomH,
            BackColor = BgGray,
        };
        var btnTop = 15;
        var btnH = 34;

        var btnAck = new Button
        {
            Text = "已知悉",
            Size = new Size(88, btnH),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            BackColor = Primary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnAck.FlatAppearance.BorderSize = 0;
        btnAck.MouseEnter += (_, _) => btnAck.BackColor = PrimaryHover;
        btnAck.MouseLeave += (_, _) => btnAck.BackColor = Primary;
        btnAck.Click += (_, _) => Resolve("ack");
        btnAck.Left = Width - btnAck.Width - pad;
        btnAck.Top = btnTop;

        var btnDetails = new Button
        {
            Text = "详情",
            Size = new Size(80, btnH),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = Color.White,
            ForeColor = Primary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnDetails.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnDetails.FlatAppearance.MouseOverBackColor = PrimaryLight;
        btnDetails.Click += (_, _) => { _timeout.Stop(); _countdown.Stop(); DetailsRequested?.Invoke(); };
        btnDetails.Left = btnAck.Left - btnDetails.Width - 10;
        btnDetails.Top = btnTop;

        var btnIgnore = new Button
        {
            Text = "忽略",
            Size = new Size(80, btnH),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = Color.White,
            ForeColor = TextSecondary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        btnIgnore.FlatAppearance.BorderColor = BorderGray;
        btnIgnore.Click += (_, _) => Resolve("ignore");
        btnIgnore.Left = btnDetails.Left - btnIgnore.Width - 10;
        btnIgnore.Top = btnTop;

        // 倒计时
        _lblCountdown = new Label
        {
            Text = "8 秒后自动关闭",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = TextMuted,
            Left = pad,
            Top = bottomH - 22,
            AutoSize = true,
        };
        bottomPanel.Controls.Add(_lblCountdown);
        bottomPanel.Controls.AddRange(new Control[] { btnIgnore, btnDetails, btnAck });

        Controls.AddRange(new Control[]
        {
            iconBox, titleLbl, closeBtn,
            div1,
            wordPanel,
            sourceLbl, catLbl,
            contentTitle, contentBox,
            bottomPanel,
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
