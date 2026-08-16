using System.IO;
using System.Windows.Forms;
using WordGuard.Core;
using WordGuard.Studio;

namespace WordGuard.Studio.App;

/// <summary>批量导入预览（PRD 用户故事 4）：解析 CSV/Excel，预览词条与问题，确认后写入词库。</summary>
public sealed class ImportForm : Form
{
    private readonly WordLibraryEditor _editor;
    private readonly WordListImporter _importer = new();
    private readonly ImportResult _result;
    private DataGridView _grid = null!;
    private ListBox _issues = null!;

    public ImportForm(WordLibraryEditor editor, string filePath, bool hasHeader)
    {
        _editor = editor;
        Text = "批量导入预览";
        Size = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;

        _result = filePath.EndsWith(".xlsx", System.StringComparison.OrdinalIgnoreCase)
            ? _importer.ImportExcel(filePath, new ImportColumnMapping { HasHeader = hasHeader })
            : _importer.ImportCsv(File.ReadAllText(filePath), new ImportColumnMapping { HasHeader = hasHeader });

        _grid = new DataGridView
        {
            Left = 12, Top = 12, Width = 524, Height = 320,
            ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("text", "文本");
        _grid.Columns.Add("cat", "分类");
        _grid.Columns.Add("sev", "严重度");
        _grid.Columns.Add("en", "启用");
        foreach (var w in _result.Words)
            _grid.Rows.Add(w.Text, w.Category, Sev(w.Severity), w.Enabled ? "是" : "否");
        Controls.Add(_grid);

        _issues = new ListBox { Left = 12, Top = 344, Width = 524, Height = 60 };
        foreach (var i in _result.Issues)
            _issues.Items.Add($"第 {i.RowNumber} 行 [{Kind(i.Kind)}] {i.Message}");
        if (_result.Issues.Count == 0) _issues.Items.Add("（无问题）");
        Controls.Add(_issues);

        var confirm = new Button { Text = $"确认导入（{_result.ImportedCount} 条）", Left = 356, Top = 412, Width = 180, Height = 32, DialogResult = DialogResult.OK };
        confirm.Click += (_, _) => Apply();
        Controls.Add(confirm);
        var cancel = new Button { Text = "取消", Left = 12, Top = 412, Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);
    }

    private void Apply()
    {
        var added = 0;
        foreach (var w in _result.Words)
            if (_editor.Add(w) == AddWordResult.Success) added++;
        MessageBox.Show($"已导入 {added} 条新词条（重复/空文本已跳过）。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string Sev(Severity s) => s switch { Severity.High => "高", Severity.Medium => "中", _ => "低" };
    private static string Kind(ImportIssueKind k) => k == ImportIssueKind.Error ? "错误" : "警告";
}
