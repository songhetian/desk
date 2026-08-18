using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WordGuard.Core;
using WordGuard.Studio;

namespace WordGuard.Studio.App;

/// <summary>批量导入预览：解析 CSV/Excel，预览词条与问题，支持追加/覆盖两种模式。</summary>
public sealed class ImportForm : Form
{
    private readonly WordLibraryEditor _editor;
    private readonly WordListImporter _importer = new();
    private readonly ImportResult _result;
    private DataGridView _grid = null!;
    private ListBox _issues = null!;
    private RadioButton _rdoAppend = null!;
    private RadioButton _rdoReplace = null!;

    private static readonly Color Primary = Color.FromArgb(59, 130, 246);
    private static readonly Color BorderGray = Color.FromArgb(229, 231, 235);
    private static readonly Color BgGray = Color.FromArgb(249, 250, 251);

    public ImportForm(WordLibraryEditor editor, string filePath, bool hasHeader)
    {
        _editor = editor;
        Text = "批量导入预览";
        Size = new Size(600, 520);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        _result = filePath.EndsWith(".xlsx", System.StringComparison.OrdinalIgnoreCase)
            ? _importer.ImportExcel(filePath, new ImportColumnMapping { HasHeader = hasHeader })
            : _importer.ImportCsv(File.ReadAllText(filePath), new ImportColumnMapping { HasHeader = hasHeader });

        BuildUi();
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "导入预览",
            Left = 20,
            Top = 16,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        Controls.Add(lblTitle);

        var lblSub = new Label
        {
            Text = $"共解析到 {_result.Words.Count} 条词条",
            Left = 20,
            Top = 40,
            AutoSize = true,
            ForeColor = Color.FromArgb(107, 114, 128),
        };
        Controls.Add(lblSub);

        var modePanel = new Panel
        {
            Left = 20,
            Top = 72,
            Width = 544,
            Height = 60,
            BackColor = BgGray,
        };
        var lblMode = new Label
        {
            Text = "导入模式：",
            Left = 16,
            Top = 12,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };
        _rdoAppend = new RadioButton
        {
            Text = "追加导入（推荐）— 新增词条，重复的自动跳过",
            Left = 16,
            Top = 34,
            AutoSize = true,
            Checked = true,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.FromArgb(55, 65, 81),
        };
        _rdoReplace = new RadioButton
        {
            Text = "覆盖导入 — 清空现有词库，完全替换",
            Left = 280,
            Top = 34,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.FromArgb(220, 38, 38),
        };
        modePanel.Controls.AddRange(new Control[] { lblMode, _rdoAppend, _rdoReplace });
        Controls.Add(modePanel);

        _grid = new DataGridView
        {
            Left = 20,
            Top = 148,
            Width = 544,
            Height = 240,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = BorderGray,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 32,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToResizeRows = false,
        };
        _grid.Columns.Add("text", "违禁词");
        _grid.Columns.Add("cat", "分类");
        _grid.Columns.Add("sev", "严重度");
        _grid.Columns.Add("en", "状态");
        _grid.Columns[0].FillWeight = 120;
        _grid.Columns[1].FillWeight = 80;
        _grid.Columns[2].FillWeight = 60;
        _grid.Columns[3].FillWeight = 50;
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            col.HeaderCell.Style.BackColor = Color.FromArgb(249, 250, 251);
            col.HeaderCell.Style.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            col.HeaderCell.Style.ForeColor = Color.FromArgb(75, 85, 99);
            col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
        }
        foreach (var w in _result.Words)
            _grid.Rows.Add(w.Text, w.Category, Sev(w.Severity), w.Enabled ? "启用" : "禁用");
        Controls.Add(_grid);

        _issues = new ListBox
        {
            Left = 20,
            Top = 400,
            Width = 544,
            Height = 52,
            BackColor = BgGray,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(107, 114, 128),
        };
        foreach (var i in _result.Issues)
            _issues.Items.Add($"第 {i.RowNumber} 行 [{Kind(i.Kind)}] {i.Message}");
        if (_result.Issues.Count == 0) _issues.Items.Add("✓ 所有词条格式正确");
        Controls.Add(_issues);

        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = BgGray,
        };
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(96, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(75, 85, 99),
        };
        btnCancel.FlatAppearance.BorderColor = BorderGray;

        var btnOk = new Button
        {
            Text = $"确认导入 {_result.Words.Count} 条",
            DialogResult = DialogResult.OK,
            Size = new Size(160, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += (_, _) => Apply();

        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            btnOk.Location = new Point(w - 192, 11);
            btnCancel.Location = new Point(w - 300, 11);
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
        Controls.Add(btnPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void Apply()
    {
        if (_rdoReplace.Checked)
        {
            if (MessageBox.Show(this,
                "覆盖模式将清空现有所有词条，确定继续？\n此操作不可撤销！",
                "确认覆盖导入",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                DialogResult = DialogResult.None;
                return;
            }
            _editor.Library.Words.Clear();
            _editor.Library.UpdatedAt = DateTime.UtcNow;
        }

        var added = 0;
        var skipped = 0;
        foreach (var w in _result.Words)
        {
            var r = _editor.Add(w);
            if (r == AddWordResult.Success) added++;
            else skipped++;
        }
        MessageBox.Show(this,
            $"导入完成！\n\n新增：{added} 条\n跳过：{skipped} 条（重复或无效）",
            "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string Sev(Severity s) => s switch { Severity.High => "高", Severity.Medium => "中", _ => "低" };
    private static string Kind(ImportIssueKind k) => k == ImportIssueKind.Error ? "错误" : "警告";
}
