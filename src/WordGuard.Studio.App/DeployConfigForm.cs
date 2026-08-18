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

    public DeployConfigForm(LibraryMetadata meta)
    {
        _meta = meta;
        Text = "部署配置（随词库下发，客户端只读）";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Size = new Size(540, 520);
        StartPosition = FormStartPosition.CenterParent;

        int y = 14;
        var title = new Label { Left = 12, Top = y, Width = 500, Height = 28,
            Text = "监控目标（EXE 名 + 可选路径；留空路径表示只按 EXE 名匹配）",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
        Controls.Add(title); y += 30;

        _grid = new DataGridView
        {
            Left = 12, Top = y, Width = 500, Height = 150,
            AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            RowHeadersVisible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _grid.Columns.Add("exe", "EXE 名 (如 WeChat.exe)");
        _grid.Columns.Add("path", "可选路径前缀 (留空=仅按 EXE 名)");
        foreach (var t in _meta.Targets)
            _grid.Rows.Add(t.ExeName, t.ExePath ?? "");
        Controls.Add(_grid); y += 158;

        _popup = new CheckBox { Left = 12, Top = y, Width = 240, Text = "弹窗提醒", Checked = _meta.AlertPopup };
        _sound = new CheckBox { Left = 280, Top = y, Width = 240, Text = "声音提醒", Checked = _meta.AlertSound };
        Controls.Add(_popup); Controls.Add(_sound); y += 30;

        _voice = new CheckBox { Left = 12, Top = y, Width = 240, Text = "语音播报", Checked = _meta.AlertVoice };
        _highlight = new CheckBox { Left = 280, Top = y, Width = 240, Text = "高亮标记", Checked = _meta.AlertHighlight };
        Controls.Add(_voice); Controls.Add(_highlight); y += 34;

        var lblSound = new Label { Left = 12, Top = y, Width = 120, Height = 22, Text = "自定义声音(wav)" };
        _soundPath = new TextBox { Left = 140, Top = y, Width = 372, Height = 22, Text = _meta.SoundFilePath ?? "" };
        Controls.Add(lblSound); Controls.Add(_soundPath); y += 32;

        var lblCool = new Label { Left = 12, Top = y, Width = 160, Height = 22, Text = "去重窗口(秒)" };
        _cooldown = new NumericUpDown { Left = 180, Top = y, Width = 90, Minimum = 0, Maximum = 3600, Value = _meta.CooldownSeconds };
        Controls.Add(lblCool); Controls.Add(_cooldown); y += 32;

        var lblRet = new Label { Left = 12, Top = y, Width = 160, Height = 22, Text = "日志保留(天)" };
        _retention = new NumericUpDown { Left = 180, Top = y, Width = 90, Minimum = 1, Maximum = 3650, Value = _meta.LogRetentionDays };
        Controls.Add(lblRet); Controls.Add(_retention); y += 44;

        var ok = new Button { Text = "确定", Left = 372, Top = y, Width = 80, Height = 30, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "取消", Left = 460, Top = y, Width = 60, Height = 30, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
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
