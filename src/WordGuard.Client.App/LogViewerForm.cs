using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 监控日志检索窗口（Premium 风格，纯 WinForms）：按时间范围 + 内容关键字检索审计日志。
/// </summary>
public sealed class LogViewerForm : Form
{
    private readonly AuditLogStore _store;
    private DateTimePicker _dpFrom = null!;
    private DateTimePicker _dpTo = null!;
    private TextBox _txtFilter = null!;
    private ListView _lvLogs = null!;
    private Label _lblCount = null!;

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);
    private static readonly Color TextGray = Color.FromArgb(86, 95, 115);

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
            Height = 56,
            BackColor = BgGray,
        };

        var lblFrom = new Label
        {
            Text = "开始日期",
            Left = 20,
            Top = 16,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _dpFrom = new DateTimePicker
        {
            Left = 88,
            Top = 12,
            Width = 130,
            Height = 28,
            Value = DateTime.Today.AddDays(-7),
            Format = DateTimePickerFormat.Short,
            BackColor = Color.White,
        };

        var lblTo = new Label
        {
            Text = "结束日期",
            Left = 236,
            Top = 16,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _dpTo = new DateTimePicker
        {
            Left = 304,
            Top = 12,
            Width = 130,
            Height = 28,
            Value = DateTime.Today,
            Format = DateTimePickerFormat.Short,
            BackColor = Color.White,
        };

        // 搜索框容器
        var searchPanel = new Panel
        {
            Left = 456,
            Top = 12,
            Width = 220,
            Height = 28,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var lblSearchIcon = new Label
        {
            Text = "🔍",
            Left = 8,
            Top = 4,
            AutoSize = true,
            ForeColor = TextGray,
        };
        _txtFilter = new TextBox
        {
            Left = 28,
            Top = 4,
            Width = 184,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9f),
            PlaceholderText = "关键词搜索...",
        };
        _txtFilter.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) LoadLogs(); };
        searchPanel.Controls.AddRange(new Control[] { lblSearchIcon, _txtFilter });

        var btnSearch = new Button
        {
            Text = "查询",
            Left = 692,
            Top = 11,
            Size = new Size(72, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        btnSearch.FlatAppearance.BorderSize = 0;
        btnSearch.MouseEnter += (_, _) => btnSearch.BackColor = PrimaryHover;
        btnSearch.MouseLeave += (_, _) => btnSearch.BackColor = Primary;
        btnSearch.Click += (_, _) => LoadLogs();

        var btnExport = new Button
        {
            Text = "导出 CSV",
            Left = 776,
            Top = 11,
            Size = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        btnExport.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnExport.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 240, 255);
        btnExport.Click += (_, _) => ExportCsv();

        topPanel.Controls.AddRange(new Control[]
        {
            lblFrom, _dpFrom, lblTo, _dpTo, searchPanel, btnSearch, btnExport,
        });

        // 日志列表（自绘 premium 风格）
        _lvLogs = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f),
            OwnerDraw = true,
        };
        _lvLogs.Columns.Add("时间", 160);
        _lvLogs.Columns.Add("严重度", 70);
        _lvLogs.Columns.Add("目标软件", 120);
        _lvLogs.Columns.Add("窗口标题", 160);
        _lvLogs.Columns.Add("命中词", 140);
        _lvLogs.Columns.Add("处理状态", 80);
        _lvLogs.Columns.Add("触发内容", 200);

        // 自绘表头
        _lvLogs.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(BgGray), e.Bounds);
            using (var pen = new Pen(BorderGray))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, e.Header.Text,
                new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                TextGray, flags);
        };
        _lvLogs.DrawItem += (_, e) => { /* 用 DrawSubItem 逐列画 */ };
        _lvLogs.DrawSubItem += (_, e) =>
        {
            var isSelected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
            var bgColor = isSelected ? Color.FromArgb(238, 240, 255) : Color.White;
            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(Color.FromArgb(238, 241, 247)))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var textRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);

            if (e.ColumnIndex == 1)
            {
                // 严重度：彩色标签
                var text = e.SubItem.Text;
                Color tagColor, tagBg;
                switch (text)
                {
                    case "高": tagColor = Color.FromArgb(229, 72, 77); tagBg = Color.FromArgb(253, 236, 236); break;
                    case "中": tagColor = Color.FromArgb(240, 140, 0); tagBg = Color.FromArgb(254, 243, 226); break;
                    default: tagColor = Color.FromArgb(79, 70, 229); tagBg = Color.FromArgb(238, 240, 255); break;
                }
                var tagRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 7, 44, e.Bounds.Height - 14);
                using (var tagBrush = new SolidBrush(tagBg))
                    e.Graphics.FillRoundedRectangle(tagBrush, tagRect, 4);
                var tagFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, text,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                    tagRect, tagColor, tagFlags);
                return;
            }

            var textColor = isSelected ? Primary : Color.FromArgb(22, 27, 38);
            if (e.ColumnIndex == 0) textColor = Color.FromArgb(86, 95, 115); // 时间列用浅灰
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, textRect, textColor, flags);
        };

        // 底部状态栏
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = BgGray,
        };
        _lblCount = new Label
        {
            Text = "共 0 条记录",
            Left = 20,
            Top = 7,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
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
