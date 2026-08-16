using System.Windows.Forms;
using WordGuard.Core;

namespace WordGuard.Studio.App;

/// <summary>单条违禁词新增/编辑对话框（PRD 管理员用户故事 1–2、6）。</summary>
public sealed class WordEditForm : Form
{
    private readonly WordEntry? _original;
    private TextBox _text = null!;
    private TextBox _category = null!;
    private ComboBox _severity = null!;
    private ComboBox _matchMode = null!;
    private CheckBox _enabled = null!;

    public WordEntry? Result { get; private set; }

    public WordEditForm(WordEntry? original = null)
    {
        _original = original;
        Text = original is null ? "新增违禁词" : "编辑违禁词";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Size = new Size(420, 280);
        StartPosition = FormStartPosition.CenterParent;

        int y = 16;
        AddLabel("违禁词文本（必填）", 12, y); y += 18;
        _text = AddTextBox(original?.Text ?? "", 12, y, 380, 22); y += 30;

        AddLabel("分类（可选）", 12, y); y += 18;
        _category = AddTextBox(original?.Category ?? "", 12, y, 380, 22); y += 30;

        AddLabel("严重级别", 12, y);
        _severity = new ComboBox { Left = 12, Top = y + 18, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _severity.Items.AddRange(new[] { "低", "中", "高" });
        _severity.SelectedIndex = original is null ? 1 : (int)original.Severity;
        Controls.Add(_severity); y += 48;

        AddLabel("匹配模式", 210, y - 48);
        _matchMode = new ComboBox { Left = 210, Top = y - 30, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _matchMode.Items.AddRange(new[] { "包含(contains)" });
        _matchMode.SelectedIndex = 0;
        Controls.Add(_matchMode); y += 6;

        _enabled = new CheckBox { Left = 12, Top = y, Width = 200, Text = "启用", Checked = original?.Enabled ?? true };
        Controls.Add(_enabled); y += 34;

        var ok = new Button { Text = "确定", Left = 226, Top = y - 6, Width = 80, Height = 30, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => Save();
        Controls.Add(ok);
        var cancel = new Button { Text = "取消", Left = 312, Top = y - 6, Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);
    }

    private void Save()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) { MessageBox.Show("违禁词文本不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result = (Result = _original) switch
        {
            not null => _original! with
            {
                Text = text,
                Category = _category.Text.Trim(),
                Severity = (Severity)_severity.SelectedIndex,
                MatchMode = MatchMode.Contains,
                Enabled = _enabled.Checked,
            },
            _ => new WordEntry
            {
                Text = text,
                Category = _category.Text.Trim(),
                Severity = (Severity)_severity.SelectedIndex,
                MatchMode = MatchMode.Contains,
                Enabled = _enabled.Checked,
            },
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private Label AddLabel(string text, int x, int y)
    {
        var l = new Label { Left = x, Top = y, Width = 380, Text = text };
        Controls.Add(l); return l;
    }

    private TextBox AddTextBox(string text, int x, int y, int w, int h)
    {
        var t = new TextBox { Left = x, Top = y, Width = w, Height = h, Text = text };
        Controls.Add(t); return t;
    }
}
