using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using WordGuard.Client;

namespace WordGuard.Client.App;

/// <summary>
/// 客户端违禁词库导入窗口（Premium 风格）：
/// 选择文件 + 追加/覆盖模式选择 + 校验预览 + 确认导入。
/// </summary>
public sealed class ImportForm : Form
{
    private readonly string _libPath;
    private TextBox _txtFile = null!;
    private RadioButton _rbAppend = null!;
    private RadioButton _rbOverwrite = null!;
    private Label _lblPreview = null!;
    private Button _btnImport = null!;
    private string? _validatedJson;
    private int _validatedCount;

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color PrimaryLight = Color.FromArgb(238, 240, 255);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);
    private static readonly Color TextGray = Color.FromArgb(86, 95, 115);
    private static readonly Color TextDark = Color.FromArgb(22, 27, 38);

    public ImportForm(string libPath)
    {
        _libPath = libPath;
        Text = "导入违禁词库";
        Size = new Size(480, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
    }

    private void BuildUi()
    {
        var y = 24;

        // 文件选择
        var lblFile = new Label
        {
            Text = "选择词库文件",
            Left = 24,
            Top = y,
            AutoSize = true,
            ForeColor = TextDark,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
        };
        y += 28;

        var filePanel = new Panel
        {
            Left = 24,
            Top = y,
            Width = 412,
            Height = 36,
            BackColor = Color.White,
        };

        _txtFile = new TextBox
        {
            Left = 0,
            Top = 4,
            Width = 320,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ReadOnly = true,
            BackColor = Color.White,
        };

        var btnBrowse = new Button
        {
            Text = "浏览...",
            Left = 328,
            Top = 3,
            Size = new Size(84, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 255);
        btnBrowse.FlatAppearance.MouseOverBackColor = PrimaryLight;
        btnBrowse.Click += (_, _) => BrowseFile();

        filePanel.Controls.AddRange(new Control[] { _txtFile, btnBrowse });
        y += 48;

        // 导入模式
        var lblMode = new Label
        {
            Text = "导入模式",
            Left = 24,
            Top = y,
            AutoSize = true,
            ForeColor = TextDark,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
        };
        y += 28;

        _rbAppend = new RadioButton
        {
            Text = "追加导入（推荐）",
            Left = 24,
            Top = y,
            AutoSize = true,
            Checked = true,
            ForeColor = TextDark,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Cursor = Cursors.Hand,
        };
        y += 26;

        var lblAppendDesc = new Label
        {
            Text = "保留现有词库，只添加新词，重复的自动跳过",
            Left = 44,
            Top = y,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        y += 22;

        _rbOverwrite = new RadioButton
        {
            Text = "覆盖导入",
            Left = 24,
            Top = y,
            AutoSize = true,
            ForeColor = TextDark,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Cursor = Cursors.Hand,
        };
        y += 26;

        var lblOverwriteDesc = new Label
        {
            Text = "清空现有词库，完全替换为新导入的内容",
            Left = 44,
            Top = y,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 8.5f),
        };
        y += 32;

        // 预览信息
        _lblPreview = new Label
        {
            Text = "请先选择词库文件",
            Left = 24,
            Top = y,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        y += 36;

        // 底部按钮栏
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
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Cursor = Cursors.Hand,
        };
        btnCancel.FlatAppearance.BorderColor = BorderGray;

        _btnImport = new Button
        {
            Text = "导入",
            Size = new Size(96, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Enabled = false,
        };
        _btnImport.FlatAppearance.BorderSize = 0;
        _btnImport.MouseEnter += (_, _) => { if (_btnImport.Enabled) _btnImport.BackColor = PrimaryHover; };
        _btnImport.MouseLeave += (_, _) => { if (_btnImport.Enabled) _btnImport.BackColor = Primary; };
        _btnImport.Click += (_, _) => DoImport();

        void LayoutBtns(object? s, EventArgs e)
        {
            var w = btnPanel.ClientSize.Width;
            _btnImport.Location = new Point(w - 132, 11);
            btnCancel.Location = new Point(w - 236, 11);
        }
        btnPanel.Resize += LayoutBtns;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, _btnImport });

        Controls.AddRange(new Control[]
        {
            lblFile, filePanel,
            lblMode, _rbAppend, lblAppendDesc, _rbOverwrite, lblOverwriteDesc,
            _lblPreview,
        });
        Controls.Add(btnPanel);
        AcceptButton = _btnImport;
        CancelButton = btnCancel;
    }

    private void BrowseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "JSON 词库文件|*.json|所有文件|*.*",
            Title = "选择违禁词库文件",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _txtFile.Text = dlg.FileName;
        ValidateFile(dlg.FileName);
    }

    private void ValidateFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var importer = new ClientLibraryImporter();
            var result = importer.Validate(json);

            if (!result.Success)
            {
                _lblPreview.Text = result.Message;
                _lblPreview.ForeColor = Color.FromArgb(229, 72, 77);
                _btnImport.Enabled = false;
                _validatedJson = null;
                return;
            }

            _validatedJson = json;
            _validatedCount = result.WordCount;
            _lblPreview.Text = $"校验通过：检测到 {result.WordCount} 条违禁词";
            _lblPreview.ForeColor = Color.FromArgb(22, 163, 74);
            _btnImport.Enabled = true;
        }
        catch (Exception ex)
        {
            _lblPreview.Text = "读取失败：" + ex.Message;
            _lblPreview.ForeColor = Color.FromArgb(229, 72, 77);
            _btnImport.Enabled = false;
            _validatedJson = null;
        }
    }

    private void DoImport()
    {
        if (string.IsNullOrEmpty(_validatedJson)) return;

        var mode = _rbAppend.Checked ? ImportMode.Append : ImportMode.Overwrite;

        if (mode == ImportMode.Overwrite)
        {
            if (MessageBox.Show(this,
                "覆盖导入会清空现有词库并替换为新内容，确定继续吗？",
                "确认覆盖",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            var importer = new ClientLibraryImporter();
            var result = importer.ImportJson(_validatedJson, _libPath, mode);

            if (result.Success)
            {
                MessageBox.Show(this, result.Message, "导入成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(this, result.Message, "导入失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "导入失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
