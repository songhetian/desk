using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Studio.App;

/// <summary>
/// 部署配置对话框：编辑随词库下发的「锁定配置」——监控目标（EXE 名 + 可选路径）、
/// 三通道开关、声音路径、去重窗口、日志保留。保存即写入词库 metadata（客户端只读）。
/// </summary>
public sealed class DeployConfigForm : Form
{
    private readonly LibraryMetadata _meta;
    private DataGridView _grid = null!;
    private CheckBox _popup = null!;
    private CheckBox _sound = null!;
    private CheckBox _voice = null!;
    private CheckBox _highlight = null!;
    private TextBox _soundPath = null!;
    private NumericUpDown _cooldown = null!;
    private NumericUpDown _retention = null!;

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);
    private static readonly Color TextGray = Color.FromArgb(86, 95, 115);

    public DeployConfigForm(LibraryMetadata meta)
    {
        _meta = meta;
        Text = "部署配置（随词库下发，客户端只读）";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false;
        Size = new Size(560, 540);
        MinimumSize = new Size(520, 500);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = new Rectangle(0, 0, ClientSize.Width, 56);
        using var brush = new LinearGradientBrush(rect,
            Color.FromArgb(246, 247, 251), Color.White, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, rect);
        using var pen = new Pen(BorderGray);
        e.Graphics.DrawLine(pen, 0, 55, ClientSize.Width, 55);
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "部署配置",
            Left = 24,
            Top = 16,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 27, 38),
        };
        Controls.Add(lblTitle);

        var lblSub = new Label
        {
            Text = "随词库下发，客户端只读",
            Left = 24,
            Top = 36,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        Controls.Add(lblSub);

        var y = 72;

        // 监控目标
        var lblTargets = new Label
        {
            Text = "监控目标",
            Left = 24,
            Top = y,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        Controls.Add(lblTargets);
        y += 26;

        _grid = new DataGridView
        {
            Left = 24,
            Top = y,
            Width = 500,
            Height = 140,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = BorderGray,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 32,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToResizeRows = false,
        };
        _grid.Columns.Add("exe", "EXE 名 (如 WeChat.exe)");
        _grid.Columns.Add("path", "可选路径前缀 (留空=仅按 EXE 名)");
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            col.HeaderCell.Style.BackColor = BgGray;
            col.HeaderCell.Style.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            col.HeaderCell.Style.ForeColor = TextGray;
            col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
        }
        foreach (var t in _meta.Targets)
            _grid.Rows.Add(t.ExeName, t.ExePath ?? "");
        Controls.Add(_grid);
        y += 148;

        // 通道开关
        _popup = new CheckBox
        {
            Left = 24, Top = y, Width = 220,
            Text = "弹窗提醒",
            Checked = _meta.AlertPopup,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(86, 95, 115),
        };
        _sound = new CheckBox
        {
            Left = 280, Top = y, Width = 220,
            Text = "声音提醒",
            Checked = _meta.AlertSound,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(86, 95, 115),
        };
        Controls.Add(_popup); Controls.Add(_sound); y += 32;

        _voice = new CheckBox
        {
            Left = 24, Top = y, Width = 220,
            Text = "语音播报",
            Checked = _meta.AlertVoice,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(86, 95, 115),
        };
        _highlight = new CheckBox
        {
            Left = 280, Top = y, Width = 220,
            Text = "高亮标记",
            Checked = _meta.AlertHighlight,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(86, 95, 115),
        };
        Controls.Add(_voice); Controls.Add(_highlight); y += 38;

        // 自定义声音
        var lblSound = new Label
        {
            Left = 24, Top = y + 3, Width = 120, Height = 22,
            Text = "自定义声音(wav)",
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        _soundPath = new TextBox
        {
            Left = 152, Top = y, Width = 372, Height = 28,
            Text = _meta.SoundFilePath ?? "",
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5f),
        };
        Controls.Add(lblSound); Controls.Add(_soundPath); y += 38;

        // 去重窗口
        var lblCool = new Label
        {
            Left = 24, Top = y + 3, Width = 140, Height = 22,
            Text = "去重窗口(秒)",
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        _cooldown = new NumericUpDown
        {
            Left = 172, Top = y, Width = 100,
            Minimum = 0, Maximum = 3600,
            Value = _meta.CooldownSeconds,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(lblCool); Controls.Add(_cooldown); y += 38;

        // 日志保留
        var lblRet = new Label
        {
            Left = 24, Top = y + 3, Width = 140, Height = 22,
            Text = "日志保留(天)",
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        _retention = new NumericUpDown
        {
            Left = 172, Top = y, Width = 100,
            Minimum = 1, Maximum = 3650,
            Value = _meta.LogRetentionDays,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(lblRet); Controls.Add(_retention);

        // 底部按钮栏
        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = BgGray,
        };
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(96, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnCancel.FlatAppearance.BorderColor = BorderGray;

        var btnOk = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Size = new Size(96, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.MouseEnter += (_, _) => btnOk.BackColor = PrimaryHover;
        btnOk.MouseLeave += (_, _) => btnOk.BackColor = Primary;
        btnOk.Click += (_, _) => Save();

        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            btnOk.Location = new Point(w - 136, 12);
            btnCancel.Location = new Point(w - 244, 12);
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
        Controls.Add(btnPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void Save()
    {
        _meta.Targets.Clear();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            var exe = (r.Cells[0].Value?.ToString() ?? "").Trim();
            var p = (r.Cells[1].Value?.ToString() ?? "").Trim();
            if (exe.Length == 0) continue;
            _meta.Targets.Add(new TargetSpec { ExeName = exe, ExePath = p.Length == 0 ? null : p });
        }
        _meta.AlertPopup = _popup.Checked;
        _meta.AlertSound = _sound.Checked;
        _meta.AlertVoice = _voice.Checked;
        _meta.AlertHighlight = _highlight.Checked;
        _meta.SoundFilePath = _soundPath.Text.Trim();
        _meta.CooldownSeconds = (int)_cooldown.Value;
        _meta.LogRetentionDays = (int)_retention.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
