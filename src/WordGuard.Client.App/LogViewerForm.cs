using System.Drawing;
using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控日志检索窗口（纯 WinForms）：按时间范围 + 内容关键字检索审计日志。
/// </summary>
public sealed class LogViewerForm : Form
{
    private readonly AuditLogStore _store;
    private DateTimePicker _dpFrom = null!;
    private DateTimePicker _dpTo = null!;
    private TextBox _txtFilter = null!;
    private ListView _lvLogs = null!;
    private Label _lblCount = null!;

    public LogViewerForm(AuditLogStore store)
    {
        _store = store;
        Text = "监控日志 — WordGuard";
        Size = new Size(960, 600);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
        LoadLogs();
    }

    private void BuildUi()
    {
        // 顶部查询栏
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(249, 250, 251),
        };

        var lblFrom = new Label
        {
            Text = "开始日期",
            Left = 0,
            Top = 14,
            Width = 60,
            AutoSize = true,
        };
        _dpFrom = new DateTimePicker
        {
            Left = 60,
            Top = 10,
            Width = 140,
            Value = DateTime.Today.AddDays(-7),
            Format = DateTimePickerFormat.Short,
        };

        var lblTo = new Label
        {
            Text = "结束日期",
            Left = 218,
            Top = 14,
            Width = 60,
            AutoSize = true,
        };
        _dpTo = new DateTimePicker
        {
            Left = 278,
            Top = 10,
            Width = 140,
            Value = DateTime.Today,
            Format = DateTimePickerFormat.Short,
        };

        var lblFilter = new Label
        {
            Text = "关键词",
            Left = 436,
            Top = 14,
            Width = 50,
            AutoSize = true,
        };
        _txtFilter = new TextBox
        {
            Left = 486,
            Top = 10,
            Width = 200,
        };

        var btnSearch = new Button
        {
            Text = "查询",
            Left = 700,
            Top = 9,
            Width = 80,
            Height = 26,
        };
        btnSearch.Click += (_, _) => LoadLogs();

        var btnExport = new Button
        {
            Text = "导出 CSV",
            Left = 790,
            Top = 9,
            Width = 90,
            Height = 26,
        };
        btnExport.Click += (_, _) => ExportCsv();

        topPanel.Controls.AddRange(new Control[]
        {
            lblFrom, _dpFrom, lblTo, _dpTo, lblFilter, _txtFilter, btnSearch, btnExport,
        });

        // 日志列表
        _lvLogs = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.White,
        };
        _lvLogs.Columns.Add("时间", 140);
        _lvLogs.Columns.Add("严重度", 60);
        _lvLogs.Columns.Add("目标软件", 120);
        _lvLogs.Columns.Add("窗口标题", 150);
        _lvLogs.Columns.Add("命中词", 120);
        _lvLogs.Columns.Add("处理状态", 80);
        _lvLogs.Columns.Add("触发内容", 200);

        // 底部状态栏
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(12, 4, 12, 4),
            BackColor = Color.FromArgb(249, 250, 251),
        };
        _lblCount = new Label
        {
            Text = "共 0 条",
            Dock = DockStyle.Left,
            AutoSize = false,
            Height = 20,
        };
        bottomPanel.Controls.Add(_lblCount);

        Controls.Add(_lvLogs);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);
    }

    private void LoadLogs()
    {
        var from = _dpFrom.Value.Date;
        var to = _dpTo.Value.Date.AddDays(1).AddTicks(-1);
        var filter = string.IsNullOrWhiteSpace(_txtFilter.Text) ? null : _txtFilter.Text.Trim();

        var rows = _store.Query(from, to, filter).ToList();

        _lvLogs.BeginUpdate();
        _lvLogs.Items.Clear();
        foreach (var e in rows)
        {
            var item = new ListViewItem(e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(e.Severity switch
            {
                Severity.High => "高",
                Severity.Medium => "中",
                _ => "低",
            });
            item.SubItems.Add(e.TargetSoftware ?? "—");
            item.SubItems.Add(e.WindowTitle ?? "—");
            item.SubItems.Add(string.Join("、", e.MatchedWords.Select(w => w.Text).Take(5)));
            item.SubItems.Add(e.Disposition ?? "—");
            item.SubItems.Add(e.TriggeredContent ?? "");

            if (e.Severity == Severity.High)
                item.ForeColor = Color.FromArgb(220, 38, 38);
            else if (e.Severity == Severity.Medium)
                item.ForeColor = Color.FromArgb(217, 119, 6);

            _lvLogs.Items.Add(item);
        }
        _lvLogs.EndUpdate();
        _lblCount.Text = $"共 {rows.Count} 条记录";
    }

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"wordguard-logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Title = "导出日志",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var from = _dpFrom.Value.Date;
            var to = _dpTo.Value.Date.AddDays(1).AddTicks(-1);
            var filter = string.IsNullOrWhiteSpace(_txtFilter.Text) ? null : _txtFilter.Text.Trim();
            var rows = _store.Query(from, to, filter).ToList();

            using var sw = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
            sw.WriteLine("时间,严重度,目标软件,窗口标题,命中词,处理状态,触发内容");
            foreach (var e in rows)
            {
                var words = string.Join("、", e.MatchedWords.Select(w => w.Text));
                sw.WriteLine($"{Csv(e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))}," +
                             $"{Csv(e.Severity.ToString())}," +
                             $"{Csv(e.TargetSoftware)}," +
                             $"{Csv(e.WindowTitle)}," +
                             $"{Csv(words)}," +
                             $"{Csv(e.Disposition)}," +
                             $"{Csv(e.TriggeredContent)}");
            }
            MessageBox.Show(this, $"已导出 {rows.Count} 条到 {dlg.FileName}", "导出成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
