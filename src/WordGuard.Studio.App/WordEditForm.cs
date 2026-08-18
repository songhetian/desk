using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Studio.App;

/// <summary>
/// 词条编辑对话框（Win11 风格，圆角卡片式）。
/// 分类使用 ComboBox 下拉选择，支持手动输入新分类。
/// </summary>
public sealed class WordEditForm : Form
{
    private TextBox _txtWord = null!;
    private ComboBox _cbCategory = null!;
    private ComboBox _cbSeverity = null!;
    private CheckBox _chkEnabled = null!;

    private readonly WordEntry? _original;
    private readonly List<string> _categoryList;

    public WordEntry? Result { get; private set; }

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);

    public WordEditForm(WordEntry? original, List<string> categoryList)
    {
        _original = original;
        _categoryList = categoryList ?? new List<string>();

        Text = original is null ? "新增词条" : "编辑词条";
        Size = new Size(520, 420);
        MinimumSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
        LoadData();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // 顶部标题栏渐变
        var rect = new Rectangle(0, 0, ClientSize.Width, 56);
        using var brush = new LinearGradientBrush(rect,
            Color.FromArgb(246, 247, 251), Color.White, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, rect);
        using var pen = new Pen(BorderGray);
        e.Graphics.DrawLine(pen, 0, 55, ClientSize.Width, 55);
    }

    private void BuildUi()
    {
        // ---- 内容区容器（带左右 padding）----
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 16, 32, 16),
        };

        var y = 0;

        // 违禁词
        var lblWord = new Label
        {
            Text = "违禁词",
            Left = 32,
            Top = 68,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        Controls.Add(lblWord);

        _txtWord = new TextBox
        {
            Left = 32,
            Top = 92,
            Width = 444,
            Height = 36,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10f),
            PlaceholderText = "请输入违禁词，如「最低价」",
        };
        Controls.Add(_txtWord);

        // 必填星号
        var lblRequired = new Label
        {
            Text = "*",
            Left = 32 + _txtWord.Width - 12,
            Top = 72,
            AutoSize = true,
            ForeColor = Color.FromArgb(229, 72, 77),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
        };
        Controls.Add(lblRequired);

        // 分类
        var lblCat = new Label
        {
            Text = "分类",
            Left = 32,
            Top = 148,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        Controls.Add(lblCat);

        _cbCategory = new ComboBox
        {
            Left = 32,
            Top = 172,
            Width = 444,
            Height = 36,
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 10f),
        };
        _cbCategory.Items.AddRange(_categoryList.ToArray());
        Controls.Add(_cbCategory);

        var lblCatHint = new Label
        {
            Text = "可直接输入新分类名",
            Left = 90,
            Top = 148,
            AutoSize = true,
            ForeColor = Color.FromArgb(138, 146, 166),
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        Controls.Add(lblCatHint);

        // 严重度
        var lblSeverity = new Label
        {
            Text = "严重度",
            Left = 32,
            Top = 228,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        Controls.Add(lblSeverity);

        _cbSeverity = new ComboBox
        {
            Left = 32,
            Top = 252,
            Width = 444,
            Height = 36,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 10f),
        };
        _cbSeverity.Items.AddRange(new object[] { "高 - 严重违规（直接拦截）", "中 - 中等违规（警告提示）", "低 - 轻微违规（记录审计）" });
        _cbSeverity.SelectedIndex = 1;
        Controls.Add(_cbSeverity);

        // 启用状态
        _chkEnabled = new CheckBox
        {
            Text = "启用该词条（关闭后不参与检测）",
            Left = 32,
            Top = 308,
            AutoSize = true,
            Checked = true,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(86, 95, 115),
        };
        Controls.Add(_chkEnabled);

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
            ForeColor = Color.FromArgb(86, 95, 115),
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnCancel.FlatAppearance.BorderColor = BorderGray;
        btnCancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 241, 247);

        var btnOk = new Button
        {
            Text = _original is null ? "添加" : "保存",
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
        btnOk.Click += (_, _) => OnOk();

        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            btnOk.Location = new Point(w - 128, 12);
            btnCancel.Location = new Point(w - 236, 12);
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
        Controls.Add(btnPanel);

        // 标题文字
        var lblTitle = new Label
        {
            Text = _original is null ? "新增词条" : "编辑词条",
            Left = 32,
            Top = 16,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 27, 38),
        };
        Controls.Add(lblTitle);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void LoadData()
    {
        if (_original is null) return;

        _txtWord.Text = _original.Text;
        _cbCategory.Text = _original.Category ?? "";
        _cbSeverity.SelectedIndex = _original.Severity switch
        {
            Severity.High => 0,
            Severity.Medium => 1,
            _ => 2,
        };
        _chkEnabled.Checked = _original.Enabled;
    }

    private void OnOk()
    {
        var word = _txtWord.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(word))
        {
            MessageBox.Show(this, "请输入违禁词", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            _txtWord.Focus();
            return;
        }

        var category = _cbCategory.Text?.Trim() ?? "";
        var severity = _cbSeverity.SelectedIndex switch
        {
            0 => Severity.High,
            1 => Severity.Medium,
            _ => Severity.Low,
        };

        if (_original is null)
        {
            Result = new WordEntry
            {
                Text = word,
                Category = category,
                Severity = severity,
                Enabled = _chkEnabled.Checked,
            };
        }
        else
        {
            Result = _original with
            {
                Text = word,
                Category = category,
                Severity = severity,
                Enabled = _chkEnabled.Checked,
            };
        }

        DialogResult = DialogResult.OK;
    }
}
