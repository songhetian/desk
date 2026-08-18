using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控状态面板（Win11 风格，纯 WinForms）。
/// - 监控设置：ListView 管理目标，支持手动添加 / 从进程选择 / 删除
/// - 词库信息：支持搜索过滤
/// - 整体采用 Win11 Mica 风格配色：浅灰背景 + 蓝色主调 + 圆角按钮
/// </summary>
public sealed class StatusForm : Form
{
    private readonly Func<LibraryFileSource> _getLib;
    private readonly Func<string> _getLibPath;
    private readonly Func<CaptureStats>? _getStats;
    private readonly Func<string>? _getDebugLogPath;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly Action _onSaved;

    private ListView _lvTargets = null!;
    private CheckBox _chkPopup = null!;
    private CheckBox _chkSound = null!;
    private CheckBox _chkVoice = null!;
    private CheckBox _chkAutoDelete = null!;
    private NumericUpDown _numCooldown = null!;
    private NumericUpDown _numRetention = null!;
    private Label _lblLibStatus = null!;
    private ListView _lvWords = null!;
    private TextBox _txtWordSearch = null!;
    private Label _lblMonitorStatus = null!;

    // 诊断面板控件
    private Label _lblDiagTicks = null!;
    private Label _lblDiagCaptured = null!;
    private Label _lblDiagAlerts = null!;
    private Label _lblDiagRate = null!;
    private Label _lblDiagLogPath = null!;
    private Label _lblDiagLastText = null!;
    private Label _lblDiagLastExe = null!;
    private Label _lblDiagLastTitle = null!;
    private Label _lblDiagLastTime = null!;
    private Label _lblDiagLastMethod = null!;
    private System.Windows.Forms.Timer? _diagTimer;

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);
    private static readonly Color TextGray = Color.FromArgb(86, 95, 115);

    public StatusForm(Func<LibraryFileSource> getLib, Func<string> getLibPath,
        AppSettings settings, string settingsPath, Action onSaved,
        Func<CaptureStats>? getStats = null, Func<string>? getDebugLogPath = null)
    {
        _getLib = getLib;
        _getLibPath = getLibPath;
        _getStats = getStats;
        _getDebugLogPath = getDebugLogPath;
        _settings = settings;
        _settingsPath = settingsPath;
        _onSaved = onSaved;

        Text = "监控状态 — WordGuard";
        Size = new Size(680, 720);
        MinimumSize = new Size(580, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
        LoadData();
    }

    private void BuildUi()
    {
        // 顶部：运行状态卡片
        var topCard = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(20, 14, 20, 14),
            BackColor = Color.FromArgb(238, 240, 255),
        };

        var statusDot = new Panel
        {
            Left = 0,
            Top = 20,
            Width = 12,
            Height = 12,
            BackColor = Color.FromArgb(34, 197, 94),
            Dock = DockStyle.Left,
            Margin = new Padding(0, 20, 0, 20),
        };

        _lblMonitorStatus = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(79, 70, 229),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "监控运行中",
            Padding = new Padding(12, 0, 0, 0),
        };

        _lblLibStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = TextGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 20,
            Text = "词库加载中...",
            Padding = new Padding(12, 0, 0, 0),
        };

        topCard.Controls.Add(_lblMonitorStatus);
        topCard.Controls.Add(_lblLibStatus);

        // TabControl
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };

        var tabSettings = new TabPage("监控设置");
        BuildSettingsTab(tabSettings);
        tabs.TabPages.Add(tabSettings);

        var tabWords = new TabPage("词库信息");
        BuildWordsTab(tabWords);
        tabs.TabPages.Add(tabWords);

        var tabDiag = new TabPage("运行诊断");
        BuildDiagTab(tabDiag);
        tabs.TabPages.Add(tabDiag);

        // 底部按钮栏
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(16, 14, 16, 14),
            BackColor = BgGray,
        };

        var btnReset = new Button
        {
            Text = "恢复默认",
            Left = 16,
            Top = 14,
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextGray,
        };
        btnReset.FlatAppearance.BorderColor = BorderGray;
        btnReset.Click += (_, _) => ResetDefaults();

        var btnClose = new Button
        {
            Text = "关闭",
            Left = 452,
            Top = 14,
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(55, 65, 81),
        };
        btnClose.FlatAppearance.BorderColor = BorderGray;
        btnClose.Click += (_, _) => Close();

        var btnSave = new Button
        {
            Text = "保存设置",
            Left = 548,
            Top = 14,
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.MouseEnter += (_, _) => btnSave.BackColor = PrimaryHover;
        btnSave.MouseLeave += (_, _) => btnSave.BackColor = Primary;
        btnSave.Click += (_, _) => SaveSettings();

        bottomPanel.Controls.AddRange(new Control[] { btnReset, btnClose, btnSave });
        AcceptButton = btnSave;
        CancelButton = btnClose;

        Controls.Add(tabs);
        Controls.Add(bottomPanel);
        Controls.Add(topCard);
    }

    private void BuildSettingsTab(TabPage tab)
    {
        tab.BackColor = Color.White;
        tab.Padding = new Padding(0);

        var y = 8;

        // ---- 监控目标区 ----
        var lblTargets = new Label
        {
            Text = "监控目标",
            Left = 20,
            Top = y,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        y += 28;

        // 目标列表 + 侧边按钮组
        _lvTargets = new ListView
        {
            Left = 20,
            Top = y,
            Size = new Size(480, 160),
            View = View.List,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            CheckBoxes = false,
        };

        var btnAdd = new Button
        {
            Text = "+ 手动添加",
            Left = 512,
            Top = y,
            Size = new Size(124, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
        };
        btnAdd.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnAdd.Click += (_, _) => AddTargetManually();

        var btnPick = new Button
        {
            Text = "从进程中选择",
            Left = 512,
            Top = y + 40,
            Size = new Size(124, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
        };
        btnPick.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnPick.Click += (_, _) => PickFromProcesses();

        var btnDelete = new Button
        {
            Text = "删除选中",
            Left = 512,
            Top = y + 80,
            Size = new Size(124, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(229, 72, 77),
        };
        btnDelete.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
        btnDelete.Click += (_, _) => DeleteSelectedTargets();

        var lblHint = new Label
        {
            Text = "常用客服软件：千牛（Qianniu.exe）、京麦（JingMai.exe）、飞鸽（Feige.exe）、微信（WeChat.exe）、企业微信（WXWork.exe）",
            Left = 20,
            Top = y + 172,
            Size = new Size(620, 20),
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        y += 200;

        tab.Controls.AddRange(new Control[]
        {
            lblTargets, _lvTargets, btnAdd, btnPick, btnDelete, lblHint,
        });

        // ---- 告警方式区 ----
        var lblAlert = new Label
        {
            Text = "告警方式",
            Left = 20,
            Top = y + 10,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        y += 38;

        var alertPanel = new Panel
        {
            Left = 20,
            Top = y,
            Width = 616,
            Height = 44,
            BackColor = BgGray,
        };
        _chkPopup = new CheckBox { Text = "桌面弹窗", Left = 16, Top = 12, AutoSize = true, Checked = true };
        _chkSound = new CheckBox { Text = "提示音", Left = 120, Top = 12, AutoSize = true, Checked = true };
        _chkVoice = new CheckBox { Text = "语音播报", Left = 210, Top = 12, AutoSize = true, Checked = true };
        _chkAutoDelete = new CheckBox { Text = "自动删除（Ctrl+A+退格）", Left = 310, Top = 12, AutoSize = true };
        alertPanel.Controls.AddRange(new Control[] { _chkPopup, _chkSound, _chkVoice, _chkAutoDelete });
        y += 54;

        // ---- 冷却 & 保留 ----
        var lblCooldown = new Label
        {
            Text = "告警冷却时间：",
            Left = 20,
            Top = y + 4,
            AutoSize = true,
            ForeColor = Color.FromArgb(55, 65, 81),
        };
        _numCooldown = new NumericUpDown
        {
            Left = 120,
            Top = y,
            Width = 100,
            Minimum = 0,
            Maximum = 3600,
            Value = 30,
        };
        var lblCooldownUnit = new Label
        {
            Text = "秒",
            Left = 226,
            Top = y + 4,
            AutoSize = true,
            ForeColor = TextGray,
        };

        var lblRetention = new Label
        {
            Text = "日志保留：",
            Left = 300,
            Top = y + 4,
            AutoSize = true,
            ForeColor = Color.FromArgb(55, 65, 81),
        };
        _numRetention = new NumericUpDown
        {
            Left = 380,
            Top = y,
            Width = 100,
            Minimum = 1,
            Maximum = 365,
            Value = 30,
        };
        var lblRetentionUnit = new Label
        {
            Text = "天",
            Left = 486,
            Top = y + 4,
            AutoSize = true,
            ForeColor = TextGray,
        };
        y += 40;

        tab.Controls.AddRange(new Control[]
        {
            lblAlert, alertPanel,
            lblCooldown, _numCooldown, lblCooldownUnit,
            lblRetention, _numRetention, lblRetentionUnit,
        });
    }

    private void BuildWordsTab(TabPage tab)
    {
        tab.BackColor = Color.White;
        tab.Padding = new Padding(0);

        // 搜索栏
        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = BgGray,
        };

        var lblSearch = new Label
        {
            Text = "搜索：",
            Left = 20,
            Top = 16,
            AutoSize = true,
            ForeColor = TextGray,
        };

        _txtWordSearch = new TextBox
        {
            Left = 64,
            Top = 12,
            Width = 400,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _txtWordSearch.TextChanged += (_, _) => FilterWords();

        var btnImport = new Button
        {
            Text = "导入词库...",
            Left = 532,
            Top = 10,
            Size = new Size(108, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
        };
        btnImport.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnImport.Click += (_, _) => ImportLibrary();

        searchPanel.Controls.AddRange(new Control[] { lblSearch, _txtWordSearch, btnImport });

        // 词库列表
        _lvWords = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
        };
        _lvWords.Columns.Add("违禁词", 180);
        _lvWords.Columns.Add("分类", 120);
        _lvWords.Columns.Add("严重度", 100);
        _lvWords.Columns.Add("状态", 80);

        tab.Controls.Add(_lvWords);
        tab.Controls.Add(searchPanel);
    }

    private void LoadData()
    {
        // 监控目标
        _lvTargets.Items.Clear();
        foreach (var t in _settings.MonitorTargets)
            _lvTargets.Items.Add(t);

        // 告警配置
        _chkPopup.Checked = _settings.AlertPopup;
        _chkSound.Checked = _settings.AlertSound;
        _chkVoice.Checked = _settings.AlertVoice;
        _chkAutoDelete.Checked = _settings.AutoDelete;
        _numCooldown.Value = _settings.CooldownSeconds;
        _numRetention.Value = _settings.LogRetentionDays;

        // 词库状态
        var lib = _getLib();
        var libPath = _getLibPath();
        var online = lib.Status.FileExists;

        if (online)
        {
            try
            {
                var wl = WordLibrary.LoadFromFile(libPath);
                var time = wl.UpdatedAt == DateTime.MinValue
                    ? "—"
                    : wl.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                _lblLibStatus.Text = $"词库：{wl.Words.Count} 条 · 更新于 {time}";
                _lblMonitorStatus.Text = _settings.MonitorTargets.Count > 0
                    ? $"监控运行中 · {_settings.MonitorTargets.Count} 个目标"
                    : "监控运行中 · 未配置目标";

                LoadWordsToList(wl.Words);
            }
            catch
            {
                _lblLibStatus.Text = "词库状态：文件损坏";
            }
        }
        else
        {
            _lblLibStatus.Text = "词库状态：未找到词库文件";
        }
    }

    private List<WordEntry> _allWords = new();

    private void LoadWordsToList(List<WordEntry> words)
    {
        _allWords = words.ToList();
        FilterWords();
    }

    private void FilterWords()
    {
        var keyword = _txtWordSearch?.Text?.Trim() ?? "";
        _lvWords.BeginUpdate();
        _lvWords.Items.Clear();

        var query = _allWords.AsEnumerable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(w =>
                w.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (w.Category?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var w in query)
        {
            var item = new ListViewItem(w.Text);
            item.SubItems.Add(w.Category ?? "—");
            item.SubItems.Add(w.Severity switch
            {
                Severity.High => "高",
                Severity.Medium => "中",
                _ => "低",
            });
            item.SubItems.Add(w.Enabled ? "启用" : "禁用");

            if (w.Severity == Severity.High)
                item.ForeColor = Color.FromArgb(220, 38, 38);
            else if (w.Severity == Severity.Medium)
                item.ForeColor = Color.FromArgb(217, 119, 6);
            if (!w.Enabled) item.ForeColor = Color.Gray;

            _lvWords.Items.Add(item);
        }
        _lvWords.EndUpdate();
    }

    // ---- 监控目标操作 ----

    private void AddTargetManually()
    {
        var input = InputDialog.Show(this, "请输入进程名（如 WeChat.exe）：", "添加监控目标", "");
        if (string.IsNullOrWhiteSpace(input)) return;

        var exeName = input.Trim();
        if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeName += ".exe";

        if (_lvTargets.Items.Cast<ListViewItem>().Any(i =>
            i.Text.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "该目标已存在", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _lvTargets.Items.Add(exeName);
    }

    private void PickFromProcesses()
    {
        using var dlg = new ProcessPickerDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var exe in dlg.SelectedExes)
        {
            if (_lvTargets.Items.Cast<ListViewItem>().Any(i =>
                i.Text.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                continue;
            _lvTargets.Items.Add(exe);
        }
    }

    private void DeleteSelectedTargets()
    {
        if (_lvTargets.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "请先选择要删除的目标", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (ListViewItem item in _lvTargets.SelectedItems)
            _lvTargets.Items.Remove(item);
    }

    private void SaveSettings()
    {
        var targets = _lvTargets.Items.Cast<ListViewItem>()
            .Select(i => i.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        _settings.MonitorTargets = targets;
        _settings.AlertPopup = _chkPopup.Checked;
        _settings.AlertSound = _chkSound.Checked;
        _settings.AlertVoice = _chkVoice.Checked;
        _settings.AutoDelete = _chkAutoDelete.Checked;
        _settings.CooldownSeconds = (int)_numCooldown.Value;
        _settings.LogRetentionDays = (int)_numRetention.Value;

        try
        {
            _settings.Save(_settingsPath);
            _onSaved();
            MessageBox.Show(this, "配置已保存并立即生效 ✓", "保存成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetDefaults()
    {
        if (MessageBox.Show(this, "确定要恢复为默认配置吗？", "确认",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _settings.MonitorTargets = ["WeChat.exe", "QQ.exe"];
        _settings.AlertPopup = true;
        _settings.AlertSound = true;
        _settings.AlertVoice = true;
        _settings.CooldownSeconds = 5;
        _settings.LogRetentionDays = 30;

        try
        {
            _settings.Save(_settingsPath);
            _onSaved();
            LoadData();
            MessageBox.Show(this, "已恢复为默认配置", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportLibrary()
    {
        using var form = new ImportForm(_getLibPath());
        if (form.ShowDialog(this) == DialogResult.OK)
            LoadData();
    }

    private void BuildDiagTab(TabPage tab)
    {
        tab.BackColor = Color.White;
        tab.Padding = new Padding(0);

        var y = 20;

        // ---- 捕获统计卡片 ----
        var statsCard = new Panel
        {
            Left = 20,
            Top = y,
            Width = 620,
            Height = 110,
            BackColor = Color.FromArgb(249, 250, 251),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var lblStatsTitle = new Label
        {
            Text = "  📊 捕获统计",
            Left = 0,
            Top = 10,
            Width = 620,
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };

        // 4 个统计项
        var items = new (string Label, string Key)[]
        {
            ("总轮询次数", "ticks"),
            ("成功捕获", "captured"),
            ("触发告警", "alerts"),
            ("捕获率", "rate"),
        };

        for (int i = 0; i < items.Length; i++)
        {
            var left = 20 + i * 150;
            var box = new Panel
            {
                Left = left,
                Top = 38,
                Width = 140,
                Height = 60,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lbl = new Label
            {
                Text = items[i].Label,
                Left = 10,
                Top = 8,
                AutoSize = true,
                ForeColor = TextGray,
                Font = new Font("Microsoft YaHei UI", 8.5f),
            };
            var val = new Label
            {
                Left = 10,
                Top = 26,
                AutoSize = true,
                ForeColor = Color.FromArgb(17, 24, 39),
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            };
            box.Controls.AddRange(new Control[] { lbl, val });
            statsCard.Controls.Add(box);

            switch (items[i].Key)
            {
                case "ticks": _lblDiagTicks = val; break;
                case "captured": _lblDiagCaptured = val; break;
                case "alerts": _lblDiagAlerts = val; break;
                case "rate": _lblDiagRate = val; break;
            }
        }

        tab.Controls.Add(statsCard);
        y += 140;

        // ---- 最近捕获详情卡片 ----
        var lastCard = new Panel
        {
            Left = 20,
            Top = y,
            Width = 620,
            Height = 130,
            BackColor = Color.FromArgb(249, 250, 251),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var lblLastTitle = new Label
        {
            Text = "  🔍 最近捕获详情",
            Left = 0,
            Top = 10,
            Width = 620,
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };

        // 捕获的文本
        var lblTextLabel = new Label
        {
            Text = "捕获文本：",
            Left = 20,
            Top = 40,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        _lblDiagLastText = new Label
        {
            Text = "（尚无捕获）",
            Left = 100,
            Top = 40,
            Width = 500,
            AutoSize = false,
            ForeColor = Color.FromArgb(17, 24, 39),
            Font = new Font("Microsoft YaHei UI", 9f),
        };

        // 进程名
        var lblExeLabel = new Label
        {
            Text = "目标进程：",
            Left = 20,
            Top = 66,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        _lblDiagLastExe = new Label
        {
            Text = "—",
            Left = 100,
            Top = 66,
            AutoSize = true,
            ForeColor = Color.FromArgb(79, 70, 229),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };

        // 窗口标题
        var lblTitleLabel = new Label
        {
            Text = "窗口标题：",
            Left = 20,
            Top = 90,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        _lblDiagLastTitle = new Label
        {
            Text = "—",
            Left = 100,
            Top = 90,
            Width = 500,
            AutoSize = false,
            ForeColor = Color.FromArgb(75, 85, 99),
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };

        // 捕获时间
        var lblTimeLabel = new Label
        {
            Text = "捕获时间：",
            Left = 320,
            Top = 66,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        _lblDiagLastTime = new Label
        {
            Text = "—",
            Left = 400,
            Top = 66,
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };

        // 捕获方式
        var lblMethodLabel = new Label
        {
            Text = "捕获方式：",
            Left = 320,
            Top = 90,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        _lblDiagLastMethod = new Label
        {
            Text = "—",
            Left = 400,
            Top = 90,
            AutoSize = true,
            ForeColor = Color.FromArgb(16, 185, 129),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };

        lastCard.Controls.AddRange(new Control[]
        {
            lblLastTitle,
            lblTextLabel, _lblDiagLastText,
            lblExeLabel, _lblDiagLastExe,
            lblTitleLabel, _lblDiagLastTitle,
            lblTimeLabel, _lblDiagLastTime,
            lblMethodLabel, _lblDiagLastMethod,
        });

        tab.Controls.Add(lastCard);
        y += 150;

        // ---- 调试日志 ----
        var lblLogTitle = new Label
        {
            Text = "📋 调试日志",
            Left = 20,
            Top = y,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        y += 28;

        _lblDiagLogPath = new Label
        {
            Left = 20,
            Top = y,
            Width = 500,
            AutoSize = false,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Text = _getDebugLogPath?.Invoke() ?? "",
        };

        var btnOpenLog = new Button
        {
            Text = "打开日志文件",
            Left = 532,
            Top = y - 4,
            Size = new Size(108, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
        };
        btnOpenLog.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnOpenLog.Click += (_, _) =>
        {
            var path = _getDebugLogPath?.Invoke();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else
                MessageBox.Show(this, "日志文件暂未生成", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        tab.Controls.AddRange(new Control[] { lblLogTitle, _lblDiagLogPath, btnOpenLog });
        y += 40;

        // ---- 排障提示 ----
        var tipTitle = new Label
        {
            Text = "💡 监听无效排查步骤",
            Left = 20,
            Top = y,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        y += 28;

        var tips = new[]
        {
            "1. 确认目标进程名正确（如 WeChat.exe、QQ.exe）",
            "2. 点击目标软件的输入框，确保光标在里面闪烁",
            "3. 在输入框里打字，观察「成功捕获」是否增长",
            "4. 增长 = UIA 正常读取输入框文本",
            "5. 不增长 = 该软件输入框 UIA 读不到，需开启键盘钩子模式",
            "6. 注意：仅当光标在输入框里时才会捕获，聊天记录不会被读取",
        };

        foreach (var tip in tips)
        {
            var lbl = new Label
            {
                Text = tip,
                Left = 20,
                Top = y,
                AutoSize = true,
                ForeColor = Color.FromArgb(75, 85, 99),
                Font = new Font("Microsoft YaHei UI", 8.5f),
            };
            tab.Controls.Add(lbl);
            y += 22;
        }

        if (_getStats is not null)
        {
            _diagTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _diagTimer.Tick += (_, _) => RefreshDiag();
            _diagTimer.Start();
        }
    }

    private void RefreshDiag()
    {
        if (_getStats is null) return;
        var stats = _getStats();
        if (stats is null) return;

        _lblDiagTicks.Text = stats.TotalTicks.ToString();
        _lblDiagCaptured.Text = stats.TextCapturedCount.ToString();
        _lblDiagAlerts.Text = stats.AlertCount.ToString();
        var rate = stats.CaptureRate * 100;
        _lblDiagRate.Text = rate.ToString("F1") + "%";

        // 最近捕获详情
        if (stats.TextCapturedCount > 0 && !string.IsNullOrEmpty(stats.LastCapturedText))
        {
            var text = stats.LastCapturedText;
            if (text.Length > 60) text = text.Substring(0, 60) + "...";
            _lblDiagLastText.Text = text;
            _lblDiagLastExe.Text = stats.LastTargetExe;
            _lblDiagLastTitle.Text = string.IsNullOrEmpty(stats.LastWindowTitle) ? "—" : stats.LastWindowTitle;
            _lblDiagLastTime.Text = stats.LastCaptureTime == DateTime.MinValue
                ? "—"
                : stats.LastCaptureTime.ToLocalTime().ToString("HH:mm:ss");
            _lblDiagLastMethod.Text = string.IsNullOrEmpty(stats.LastCaptureMethod) ? "—" : stats.LastCaptureMethod;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _diagTimer?.Stop();
        _diagTimer?.Dispose();
        base.OnFormClosing(e);
    }
}

/// <summary>Premium 风格输入对话框。</summary>
internal static class InputDialog
{
    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);

    public static string? Show(IWin32Window owner, string prompt, string title, string defaultValue)
    {
        using var form = new Form
        {
            Text = title,
            Size = new Size(400, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
        };

        var lblPrompt = new Label
        {
            Text = prompt,
            Left = 24,
            Top = 24,
            AutoSize = true,
            ForeColor = Color.FromArgb(22, 27, 38),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
        };

        var textBox = new TextBox
        {
            Left = 24,
            Top = 56,
            Width = 336,
            Height = 32,
            Text = defaultValue,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10f),
        };

        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = BgGray,
        };

        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(86, 95, 115),
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnCancel.FlatAppearance.BorderColor = BorderGray;

        var btnOk = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.MouseEnter += (_, _) => btnOk.BackColor = PrimaryHover;
        btnOk.MouseLeave += (_, _) => btnOk.BackColor = Primary;

        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            btnOk.Location = new Point(w - 120, 10);
            btnCancel.Location = new Point(w - 216, 10);
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });

        form.Controls.AddRange(new Control[] { lblPrompt, textBox, btnPanel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;
        return form.ShowDialog(owner) == DialogResult.OK ? textBox.Text : null;
    }
}
