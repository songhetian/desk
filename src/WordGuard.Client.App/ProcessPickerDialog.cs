using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WordGuard.Client.App;

/// <summary>
/// 进程选择对话框：列出当前正在运行的进程，支持搜索，用户可勾选添加为监控目标。
/// Win11 风格设计：圆角、柔和配色、清晰的层次。
/// </summary>
public sealed class ProcessPickerDialog : Form
{
    private TextBox _txtSearch = null!;
    private ListView _lvProcesses = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    private List<ProcessInfo> _allProcesses = new();

    /// <summary>用户选中的进程名（带 .exe 后缀）列表。</summary>
    public List<string> SelectedExes { get; private set; } = new();

    public ProcessPickerDialog()
    {
        Text = "选择监控目标进程";
        Size = new Size(580, 520);
        MinimumSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;

        BuildUi();
        LoadProcesses();
    }

    private void BuildUi()
    {
        // 顶部搜索栏
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Color.FromArgb(249, 250, 251),
        };

        var lblSearch = new Label
        {
            Text = "搜索进程：",
            Left = 0,
            Top = 16,
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
        };

        _txtSearch = new TextBox
        {
            Left = 76,
            Top = 12,
            Width = 360,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _txtSearch.TextChanged += (_, _) => FilterProcesses();

        var btnRefresh = new Button
        {
            Text = "刷新",
            Left = 444,
            Top = 11,
            Size = new Size(72, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(59, 130, 246),
        };
        btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
        btnRefresh.Click += (_, _) => LoadProcesses();

        topPanel.Controls.AddRange(new Control[] { lblSearch, _txtSearch, btnRefresh });

        // 进程列表
        _lvProcesses = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            CheckBoxes = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
        };
        _lvProcesses.Columns.Add("进程名", 200);
        _lvProcesses.Columns.Add("PID", 80);
        _lvProcesses.Columns.Add("窗口标题", 260);

        // 底部按钮
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Color.FromArgb(249, 250, 251),
        };

        _btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Left = 372,
            Top = 12,
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(75, 85, 99),
        };
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);

        _btnOk = new Button
        {
            Text = "添加选中",
            DialogResult = DialogResult.OK,
            Left = 468,
            Top = 12,
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += (_, _) => CollectSelected();

        bottomPanel.Controls.AddRange(new Control[] { _btnCancel, _btnOk });

        Controls.Add(_lvProcesses);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private void LoadProcesses()
    {
        _allProcesses.Clear();
        try
        {
            var procs = Process.GetProcesses();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in procs)
            {
                try
                {
                    var name = p.ProcessName + ".exe";
                    // 去重：同名进程只保留一个（有窗口标题的优先）
                    if (seen.Contains(name)) continue;
                    seen.Add(name);

                    var title = "";
                    try { title = p.MainWindowTitle; } catch { }

                    _allProcesses.Add(new ProcessInfo(
                        Exe: name,
                        Pid: p.Id,
                        Title: title ?? "",
                        HasWindow: !string.IsNullOrEmpty(title)
                    ));
                }
                catch { }
            }
        }
        catch { }

        // 排序：有窗口的在前，然后按名称字母序
        _allProcesses = _allProcesses
            .OrderByDescending(p => p.HasWindow)
            .ThenBy(p => p.Exe, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FilterProcesses();
    }

    private void FilterProcesses()
    {
        var keyword = _txtSearch.Text?.Trim() ?? "";
        _lvProcesses.BeginUpdate();
        _lvProcesses.Items.Clear();

        foreach (var p in _allProcesses)
        {
            if (!string.IsNullOrEmpty(keyword) &&
                p.Exe.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0 &&
                p.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var item = new ListViewItem(p.Exe) { Checked = false };
            item.SubItems.Add(p.Pid.ToString());
            item.SubItems.Add(p.Title);
            if (!p.HasWindow) item.ForeColor = Color.FromArgb(156, 163, 175);
            _lvProcesses.Items.Add(item);
        }

        _lvProcesses.EndUpdate();
    }

    private void CollectSelected()
    {
        SelectedExes.Clear();
        foreach (ListViewItem item in _lvProcesses.Items)
        {
            if (item.Checked)
                SelectedExes.Add(item.Text);
        }
    }

    private sealed record ProcessInfo(string Exe, int Pid, string Title, bool HasWindow);
}
