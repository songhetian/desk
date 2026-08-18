using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WordGuard.Core;
using WordGuard.Studio;

namespace WordGuard.Studio.App;

/// <summary>
/// 词库管理端主窗口（Win11 风格，纯 WinForms）。
/// 左侧分类列表（带操作按钮），右侧词库表格（支持搜索/筛选），顶部工具栏，底部状态栏。
/// </summary>
public sealed class MainForm : Form
{
    private readonly string _path;
    private readonly WordLibrary _lib;
    private readonly WordLibraryEditor _editor;

    private ListBox _lbCategories = null!;
    private ListView _lvWords = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cbSeverityFilter = null!;
    private ToolStripStatusLabel _lblStatus = null!;
    private ToolStripStatusLabel _lblStats = null!;
    private Panel _pnlPagination = null!;
    private Label _lblPageInfo = null!;
    private Button _btnPrevPage = null!;
    private Button _btnNextPage = null!;
    private CheckBox _chkSelectAll = null!;
    private string _currentCategory = "全部";

    private const int PageSize = 50;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private List<WordEntry> _filteredWords = new();

    private static readonly Color Primary = Color.FromArgb(79, 70, 229);
    private static readonly Color PrimaryHover = Color.FromArgb(99, 102, 241);
    private static readonly Color BorderGray = Color.FromArgb(231, 233, 240);
    private static readonly Color BgGray = Color.FromArgb(246, 247, 251);
    private static readonly Color TextGray = Color.FromArgb(86, 95, 115);
    private static readonly Color Danger = Color.FromArgb(229, 72, 77);

    public MainForm(string path)
    {
        _path = path;
        _lib = WordLibrary.LoadFromFile(path);
        _editor = new WordLibraryEditor(_lib);
        _lastSavedAt = _lib.UpdatedAt;

        Text = "词库管理端 — WordGuard Studio";
        ShowInTaskbar = true;
        Size = new Size(1100, 700);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        BuildUi();
        RefreshCategories();
        RefreshWords();
        UpdateStatus();
    }

    private DateTime _lastSavedAt = DateTime.MinValue;

    private void BuildUi()
    {
        // ---- 主分割容器：左侧导航 + 右侧内容 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 260,
            FixedPanel = FixedPanel.Panel1,
            BorderStyle = BorderStyle.None,
        };
        split.Panel1.BackColor = Color.FromArgb(246, 247, 251);
        split.Panel2.BackColor = Color.White;

        // ================= 左侧分类导航 =================
        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

        // 左侧头部 Logo 区
        var leftHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Color.FromArgb(246, 247, 251),
            Padding = new Padding(20, 0, 16, 0),
        };
        var lblBrand = new Label
        {
            Text = "📚  词库管理",
            Left = 20,
            Top = 16,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 27, 38),
        };
        leftHeader.Controls.Add(lblBrand);

        // 分类列表（自绘 Win11 风格）
        _lbCategories = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(246, 247, 251),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 36,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Padding(4, 8, 4, 8),
        };
        _lbCategories.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var item = _lbCategories.Items[e.Index] as CategoryItem;
            if (item is null) return;

            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var isHover = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;

            // 背景
            Color bgColor;
            if (isSelected) bgColor = Color.FromArgb(238, 240, 255);
            else if (isHover) bgColor = Color.FromArgb(238, 241, 247);
            else bgColor = Color.FromArgb(246, 247, 251);

            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRoundedRectangle(brush, e.Bounds, 6);

            // 选中时左侧蓝色指示条
            if (isSelected)
            {
                using (var bar = new SolidBrush(Primary))
                    e.Graphics.FillRoundedRectangle(bar,
                        new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 8, 4, e.Bounds.Height - 16), 2);
            }

            // 文本
            var textColor = isSelected ? Primary : Color.FromArgb(86, 95, 115);
            var textRect = new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, item.DisplayText, e.Font, textRect, textColor, flags);
        };
        _lbCategories.SelectedIndexChanged += (_, _) =>
        {
            if (_lbCategories.SelectedItem is CategoryItem item)
            {
                _currentCategory = item.IsAll ? "全部" : item.Name;
                _currentPage = 1;
                RefreshWords();
            }
        };

        // 左侧底部操作按钮
        var leftFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.FromArgb(246, 247, 251),
            Padding = new Padding(12, 8, 12, 8),
        };

        var btnAddCat = new Button
        {
            Text = "＋ 新建分类",
            Left = 12,
            Top = 8,
            Size = new Size(108, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnAddCat.FlatAppearance.BorderColor = Color.FromArgb(238, 240, 255);
        btnAddCat.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 240, 255);
        btnAddCat.Click += (_, _) => AddCategory();

        var btnDelCat = new Button
        {
            Text = "删除",
            Left = 128,
            Top = 8,
            Size = new Size(56, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Danger,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnDelCat.FlatAppearance.BorderColor = Color.FromArgb(248, 210, 210);
        btnDelCat.FlatAppearance.MouseOverBackColor = Color.FromArgb(253, 236, 236);
        btnDelCat.Click += (_, _) => DeleteCategory();

        leftFooter.Controls.AddRange(new Control[] { btnAddCat, btnDelCat });

        leftPanel.Controls.Add(_lbCategories);
        leftPanel.Controls.Add(leftFooter);
        leftPanel.Controls.Add(leftHeader);
        split.Panel1.Controls.Add(leftPanel);

        // ================= 右侧内容区 =================
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

        // ---- 顶部工具栏 ----
        var toolBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Color.White,
        };

        // 主按钮：新增词条
        var btnAdd = new Button
        {
            Text = "＋ 新增词条",
            Left = 20,
            Top = 11,
            Size = new Size(120, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnAdd.FlatAppearance.BorderSize = 0;
        btnAdd.FlatAppearance.MouseOverBackColor = PrimaryHover;
        btnAdd.Cursor = Cursors.Hand;
        btnAdd.Click += (_, _) => AddWord();

        // 批量操作：编辑、删除
        var btnEdit = MakeToolbarButton("编辑", 152, Color.FromArgb(86, 95, 115));
        btnEdit.Click += (_, _) => EditSelected();

        var btnDelete = MakeToolbarButton("删除", 234, Danger);
        btnDelete.Click += (_, _) => DeleteSelected();

        // 分隔竖线
        var sep1 = new Panel
        {
            Left = 316,
            Top = 16,
            Width = 1,
            Height = 24,
            BackColor = BorderGray,
        };

        // 批量启用/禁用
        var btnEnable = MakeToolbarButton("启用", 328, Color.FromArgb(47, 158, 68));
        btnEnable.Click += (_, _) => SetSelectedEnabled(true);

        var btnDisable = MakeToolbarButton("禁用", 410, TextGray);
        btnDisable.Click += (_, _) => SetSelectedEnabled(false);

        // 分隔竖线 2
        var sep2 = new Panel
        {
            Left = 492,
            Top = 16,
            Width = 1,
            Height = 24,
            BackColor = BorderGray,
        };

        // 导入下拉
        var btnImport = MakeToolbarButton("导入 ▾", 504, Primary);
        var importMenu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9f), RenderMode = ToolStripRenderMode.System };
        importMenu.Items.Add("从 Excel 导入", null, (_, _) => ImportExcel());
        importMenu.Items.Add("从 CSV 导入", null, (_, _) => ImportCsv());
        importMenu.Items.Add(new ToolStripSeparator());
        importMenu.Items.Add("下载 Excel 导入模板", null, (_, _) => DownloadTemplate());
        btnImport.Click += (_, _) => importMenu.Show(btnImport, new Point(0, btnImport.Height));

        // 导出下拉
        var btnExport = MakeToolbarButton("导出 ▾", 596, Primary);
        var exportMenu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9f), RenderMode = ToolStripRenderMode.System };
        exportMenu.Items.Add("导出为 JSON（客户端用）", null, (_, _) => Export());
        exportMenu.Items.Add("导出为 Excel", null, (_, _) => ExportExcel());
        exportMenu.Items.Add(new ToolStripSeparator());
        exportMenu.Items.Add("同步到客户端目录", null, (_, _) => SyncToClient());
        btnExport.Click += (_, _) => exportMenu.Show(btnExport, new Point(0, btnExport.Height));

        // 右侧：保存 + 部署配置
        var btnDeploy = new Button
        {
            Text = "部署配置",
            Left = 860,
            Top = 11,
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        btnDeploy.FlatAppearance.BorderColor = Color.FromArgb(238, 240, 255);
        btnDeploy.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 240, 255);
        btnDeploy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnDeploy.Click += (_, _) => ShowDeployConfig();

        var btnSave = new Button
        {
            Text = "💾 保存",
            Left = 748,
            Top = 11,
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 241, 247),
            ForeColor = Color.FromArgb(86, 95, 115),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        };
        btnSave.FlatAppearance.BorderColor = BorderGray;
        btnSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 241, 247);
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.Click += (_, _) => Save();

        toolBar.Controls.AddRange(new Control[]
        {
            btnAdd, btnEdit, btnDelete, sep1,
            btnEnable, btnDisable, sep2,
            btnImport, btnExport,
            btnSave, btnDeploy,
        });

        // ---- 搜索筛选栏 ----
        var filterBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(246, 247, 251),
        };

        // 搜索框
        var searchPanel = new Panel
        {
            Left = 20,
            Top = 10,
            Width = 320,
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
        _txtSearch = new TextBox
        {
            Left = 30,
            Top = 4,
            Width = 280,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9f),
            PlaceholderText = "搜索违禁词...",
        };
        _txtSearch.TextChanged += (_, _) => { _currentPage = 1; RefreshWords(); };
        searchPanel.Controls.AddRange(new Control[] { lblSearchIcon, _txtSearch });

        // 严重度筛选
        var lblFilter = new Label
        {
            Text = "严重度：",
            Left = 360,
            Top = 14,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _cbSeverityFilter = new ComboBox
        {
            Left = 416,
            Top = 10,
            Width = 100,
            Height = 28,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
        };
        _cbSeverityFilter.Items.AddRange(new object[] { "全部级别", "高", "中", "低" });
        _cbSeverityFilter.SelectedIndex = 0;
        _cbSeverityFilter.SelectedIndexChanged += (_, _) => { _currentPage = 1; RefreshWords(); };

        filterBar.Controls.AddRange(new Control[] { searchPanel, lblFilter, _cbSeverityFilter });

        // ---- 词库列表 ----
        _lvWords = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f),
            OwnerDraw = true,
            CheckBoxes = true,
        };
        _lvWords.Columns.Add("", 36); // 复选框列
        _lvWords.Columns.Add("违禁词", 280);
        _lvWords.Columns.Add("分类", 160);
        _lvWords.Columns.Add("严重度", 120);
        _lvWords.Columns.Add("状态", 120);
        _lvWords.Columns.Add("操作", 120);
        _lvWords.DoubleClick += (_, _) => EditSelected();
        _lvWords.ItemCheck += (_, e) =>
        {
            // 延迟更新全选框状态
            _lvWords.BeginInvoke(() => UpdateSelectAllState());
        };

        // 全选复选框（画在列头位置）
        _chkSelectAll = new CheckBox
        {
            Text = "",
            Size = new Size(16, 16),
            BackColor = Color.FromArgb(246, 247, 251),
        };
        _chkSelectAll.CheckedChanged += (_, _) =>
        {
            var check = _chkSelectAll.Checked;
            foreach (ListViewItem item in _lvWords.Items)
                item.Checked = check;
        };

        // 自绘列表头（Win11 风格）
        _lvWords.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(246, 247, 251)), e.Bounds);
            using (var pen = new Pen(BorderGray))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            if (e.ColumnIndex == 0)
            {
                // 把全选复选框移动到列头位置
                _chkSelectAll.Location = new Point(e.Bounds.X + 10, e.Bounds.Y + (e.Bounds.Height - 16) / 2);
                if (!_lvWords.Controls.Contains(_chkSelectAll))
                    _lvWords.Controls.Add(_chkSelectAll);
                return;
            }

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, e.Header.Text,
                new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Color.FromArgb(86, 95, 115), flags);
        };
        _lvWords.DrawItem += (_, e) => { /* 用 DrawSubItem 逐列画 */ };
        _lvWords.DrawSubItem += (_, e) =>
        {
            var isSelected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
            var bgColor = isSelected ? Color.FromArgb(238, 240, 255) : Color.White;
            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, e.Bounds);

            // 底部细线分隔
            using (var pen = new Pen(Color.FromArgb(238, 241, 247)))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            var textRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);

            if (e.ColumnIndex == 0)
            {
                // 自绘画复选框（OwnerDraw 模式下系统不会自动画）
                var state = e.Item.Checked ? ButtonState.Checked : ButtonState.Normal;
                var cbSize = new Size(16, 16);
                var cbRect = new Rectangle(
                    e.Bounds.X + (e.Bounds.Width - cbSize.Width) / 2,
                    e.Bounds.Y + (e.Bounds.Height - cbSize.Height) / 2,
                    cbSize.Width, cbSize.Height);
                ControlPaint.DrawCheckBox(e.Graphics, cbRect, state);
                return;
            }
            else if (e.ColumnIndex == 3)
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
                var tagRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 8, 52, e.Bounds.Height - 16);
                using (var tagBrush = new SolidBrush(tagBg))
                    e.Graphics.FillRoundedRectangle(tagBrush, tagRect, 4);
                var tagFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, text,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                    tagRect, tagColor, tagFlags);
            }
            else if (e.ColumnIndex == 4)
            {
                // 状态：启用/禁用 圆点 + 文字
                var text = e.SubItem.Text;
                var isEnabled = text == "启用";
                var stateColor = isEnabled ? Color.FromArgb(47, 158, 68) : TextGray;
                var dotRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + e.Bounds.Height / 2 - 4, 8, 8);
                using (var dotBrush = new SolidBrush(stateColor))
                    e.Graphics.FillEllipse(dotBrush, dotRect);
                var stateRect = new Rectangle(e.Bounds.X + 26, e.Bounds.Y, e.Bounds.Width - 26, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, e.Item.Font, stateRect, stateColor, flags);
            }
            else if (e.ColumnIndex == 5)
            {
                // 操作列：行内切换按钮
                var tag = e.Item.Tag;
                if (tag is not Guid id) return;
                var word = _lib.Words.FirstOrDefault(w => w.Id == id);
                if (word is null) return;

                var btnText = word.Enabled ? "禁用" : "启用";
                var btnW = 52;
                var btnH = 24;
                var btnX = e.Bounds.X + 12;
                var btnY = e.Bounds.Y + (e.Bounds.Height - btnH) / 2;
                var btnRect = new Rectangle(btnX, btnY, btnW, btnH);

                Color btnBg, btnFg, btnBorder;
                if (word.Enabled)
                {
                    btnBg = Color.FromArgb(253, 236, 236);
                    btnFg = Color.FromArgb(229, 72, 77);
                    btnBorder = Color.FromArgb(248, 210, 210);
                }
                else
                {
                    btnBg = Color.FromArgb(231, 246, 236);
                    btnFg = Color.FromArgb(47, 158, 68);
                    btnBorder = Color.FromArgb(189, 233, 200);
                }

                using (var bgBrush = new SolidBrush(btnBg))
                    e.Graphics.FillRoundedRectangle(bgBrush, btnRect, 4);
                using (var borderPen = new Pen(btnBorder))
                {
                    var r = btnRect;
                    r.Width -= 1;
                    r.Height -= 1;
                    e.Graphics.DrawRoundedRectangle(borderPen, r, 4);
                }
                var btnFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, btnText,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                    btnRect, btnFg, btnFlags);
            }
            else
            {
                var textColor = isSelected ? Color.FromArgb(79, 70, 229) : Color.FromArgb(22, 27, 38);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, textRect, textColor, flags);
            }
        };

        // 点击操作列切换启用/禁用，点击复选框列切换选中
        _lvWords.MouseClick += (_, e) =>
        {
            var info = _lvWords.HitTest(e.Location);
            if (info.Item is null || info.SubItem is null) return;

            // 找到点击的列索引
            int colIndex = -1;
            for (int i = 0; i < info.Item.SubItems.Count; i++)
            {
                if (ReferenceEquals(info.Item.SubItems[i], info.SubItem))
                {
                    colIndex = i;
                    break;
                }
            }

            if (colIndex == 0)
            {
                // 复选框列：切换选中状态
                info.Item.Checked = !info.Item.Checked;
                return;
            }

            if (colIndex != 5) return; // 操作列

            if (info.Item.Tag is not Guid id) return;
            var word = _lib.Words.FirstOrDefault(w => w.Id == id);
            if (word is null) return;
            var idx = _lib.Words.FindIndex(w => w.Id == id);
            if (idx >= 0)
            {
                _lib.Words[idx] = word with { Enabled = !word.Enabled };
                _lib.UpdatedAt = DateTime.UtcNow;
                Save();
                RefreshWords();
            }
        };

        rightPanel.Controls.Add(_lvWords);
        rightPanel.Controls.Add(filterBar);
        rightPanel.Controls.Add(toolBar);

        // ---- 底部分页栏 ----
        _pnlPagination = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = Color.White,
        };
        _lblPageInfo = new Label
        {
            Text = "第 0 / 0 页",
            Left = 20,
            Top = 12,
            AutoSize = true,
            ForeColor = TextGray,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _btnPrevPage = new Button
        {
            Text = "上一页",
            Size = new Size(72, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(86, 95, 115),
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        _btnPrevPage.FlatAppearance.BorderColor = BorderGray;
        _btnPrevPage.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 251);
        _btnPrevPage.Click += (_, _) =>
        {
            if (_currentPage > 1) { _currentPage--; RefreshWords(); }
        };

        _btnNextPage = new Button
        {
            Text = "下一页",
            Size = new Size(72, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(86, 95, 115),
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        _btnNextPage.FlatAppearance.BorderColor = BorderGray;
        _btnNextPage.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 251);
        _btnNextPage.Click += (_, _) =>
        {
            if (_currentPage < _totalPages) { _currentPage++; RefreshWords(); }
        };

        void LayoutPagination(object? s, EventArgs e)
        {
            var w = _pnlPagination.ClientSize.Width;
            _btnNextPage.Location = new Point(w - 92, 8);
            _btnPrevPage.Location = new Point(w - 172, 8);
        }
        _pnlPagination.Resize += LayoutPagination;
        _pnlPagination.Controls.AddRange(new Control[] { _lblPageInfo, _btnPrevPage, _btnNextPage });

        rightPanel.Controls.Add(_pnlPagination);
        split.Panel2.Controls.Add(rightPanel);

        // ---- 底部状态栏 ----
        var statusBar = new StatusStrip
        {
            BackColor = Color.FromArgb(246, 247, 251),
            SizingGrip = false,
        };
        _lblStatus = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblStats = new ToolStripStatusLabel("共 0 条");
        statusBar.Items.Add(_lblStatus);
        statusBar.Items.Add(_lblStats);

        Controls.Add(split);
        Controls.Add(statusBar);

        // 保存提示
        FormClosing += (_, e) =>
        {
            if (_lib.UpdatedAt != _lastSavedAt)
            {
                var r = MessageBox.Show(this, "有未保存的修改，是否保存？", "确认退出",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) Save();
            }
        };
    }

    private Button MakeToolbarButton(string text, int left, Color foreColor)
    {
        var btn = new Button
        {
            Text = text,
            Left = left,
            Top = 11,
            Size = new Size(72, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = foreColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = BorderGray;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 251);
        return btn;
    }

    /// <summary>分类列表项（用于自绘）。</summary>
    private sealed class CategoryItem
    {
        public string Name { get; }
        public int Count { get; }
        public bool IsAll { get; }
        public string DisplayText => IsAll ? $"  全部  ({Count})" : $"  {Name}  ({Count})";

        public CategoryItem(string name, int count, bool isAll)
        {
            Name = name;
            Count = count;
            IsAll = isAll;
        }

        public override string ToString() => DisplayText;
    }

    private void RefreshCategories()
    {
        _lbCategories.BeginUpdate();
        _lbCategories.Items.Clear();

        var total = _lib.Words.Count;
        _lbCategories.Items.Add(new CategoryItem("全部", total, true));

        var cats = _editor.GetCategories();
        foreach (var c in cats)
        {
            _lbCategories.Items.Add(new CategoryItem(c.Name, c.Count, false));
        }

        if (_lbCategories.Items.Count > 0)
            _lbCategories.SelectedIndex = 0;

        _lbCategories.EndUpdate();
    }

    private void RefreshWords()
    {
        _lvWords.BeginUpdate();
        _lvWords.Items.Clear();

        var keyword = _txtSearch?.Text?.Trim() ?? "";
        var severityFilter = _cbSeverityFilter?.SelectedItem?.ToString() ?? "全部级别";

        var words = _lib.Words.AsEnumerable();

        if (_currentCategory != "全部")
            words = words.Where(w => w.Category == _currentCategory);

        if (severityFilter != "全部级别")
        {
            var sev = severityFilter switch
            {
                "高" => Severity.High,
                "中" => Severity.Medium,
                _ => Severity.Low,
            };
            words = words.Where(w => w.Severity == sev);
        }

        if (!string.IsNullOrEmpty(keyword))
            words = words.Where(w =>
                w.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (w.Category?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));

        _filteredWords = words.ToList();

        // 计算分页
        _totalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredWords.Count / PageSize));
        if (_currentPage > _totalPages) _currentPage = _totalPages;
        if (_currentPage < 1) _currentPage = 1;

        var pageWords = _filteredWords
            .Skip((_currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        foreach (var w in pageWords)
        {
            var item = new ListViewItem("") { Tag = w.Id };
            item.SubItems.Add(w.Text);
            item.SubItems.Add(string.IsNullOrEmpty(w.Category) ? "—" : w.Category);
            item.SubItems.Add(w.Severity switch
            {
                Severity.High => "高",
                Severity.Medium => "中",
                _ => "低",
            });
            item.SubItems.Add(w.Enabled ? "启用" : "禁用");
            item.SubItems.Add(""); // 操作列（自绘按钮）

            _lvWords.Items.Add(item);
        }
        _lvWords.EndUpdate();
        UpdatePagination();
        UpdateSelectAllState();
        UpdateStatus();
    }

    private void UpdatePagination()
    {
        if (_lblPageInfo is null || _btnPrevPage is null || _btnNextPage is null) return;
        _lblPageInfo.Text = $"第 {_currentPage} / {_totalPages} 页  ·  共 {_filteredWords.Count} 条";
        _btnPrevPage.Enabled = _currentPage > 1;
        _btnNextPage.Enabled = _currentPage < _totalPages;
        _btnPrevPage.ForeColor = _btnPrevPage.Enabled ? Color.FromArgb(86, 95, 115) : Color.FromArgb(138, 146, 166);
        _btnNextPage.ForeColor = _btnNextPage.Enabled ? Color.FromArgb(86, 95, 115) : Color.FromArgb(138, 146, 166);
    }

    private void UpdateSelectAllState()
    {
        if (_chkSelectAll is null || _lvWords is null) return;
        var total = _lvWords.Items.Count;
        var checkedCount = 0;
        foreach (ListViewItem item in _lvWords.Items)
            if (item.Checked) checkedCount++;

        _chkSelectAll.Checked = total > 0 && checkedCount == total;
    }

    private List<Guid> GetCheckedIds()
    {
        var ids = new List<Guid>();
        foreach (ListViewItem item in _lvWords.Items)
        {
            if (item.Checked && item.Tag is Guid id)
                ids.Add(id);
        }
        return ids;
    }

    private void UpdateStatus()
    {
        var total = _lib.Words.Count;
        var enabled = _lib.Words.Count(w => w.Enabled);
        var cats = _editor.GetCategories().Count;
        var filtered = _lvWords.Items.Count;
        var dirty = _lib.UpdatedAt != _lastSavedAt ? "  ·  未保存" : "";
        _lblStatus.Text = $"共 {total} 条（启用 {enabled}） · {cats} 个分类{dirty}";
        _lblStats.Text = $"显示 {filtered} 条";
    }

    private void AddWord()
    {
        using var f = new WordEditForm(null, _editor.GetCategories().Select(c => c.Name).ToList());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (f.Result is null) return;
        var result = _editor.Add(f.Result);
        if (result != AddWordResult.Success)
        {
            MessageBox.Show(this, result.ToString(), "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Save();
        RefreshCategories();
        RefreshWords();
    }

    private void EditSelected()
    {
        var ids = GetCheckedIds();
        if (ids.Count == 0)
        {
            // 没有勾选，尝试用选中行
            if (_lvWords.SelectedItems.Count == 0) return;
            if (_lvWords.SelectedItems[0].Tag is not Guid id) return;
            ids = new List<Guid> { id };
        }

        // 多选时只允许编辑第一个（或提示批量改分类/严重度）
        if (ids.Count > 1)
        {
            MessageBox.Show(this, "多选状态下请使用「批量修改」功能。单条编辑请双击词条。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var word = _lib.Words.FirstOrDefault(w => w.Id == ids[0]);
        if (word is null) return;

        using var f = new WordEditForm(word, _editor.GetCategories().Select(c => c.Name).ToList());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (f.Result is null) return;
        _editor.Update(ids[0], f.Result);
        Save();
        RefreshCategories();
        RefreshWords();
    }

    private void DeleteSelected()
    {
        var ids = GetCheckedIds();
        if (ids.Count == 0)
        {
            if (_lvWords.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvWords.SelectedItems)
                if (item.Tag is Guid id) ids.Add(id);
        }
        if (ids.Count == 0) return;

        if (MessageBox.Show(this, $"确定删除选中的 {ids.Count} 条词条？", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var idSet = new HashSet<Guid>(ids);
        _lib.Words.RemoveAll(w => idSet.Contains(w.Id));
        _lib.UpdatedAt = DateTime.UtcNow;
        Save();
        RefreshCategories();
        RefreshWords();
    }

    private void SetSelectedEnabled(bool enabled)
    {
        var ids = GetCheckedIds();
        if (ids.Count == 0)
        {
            foreach (ListViewItem item in _lvWords.SelectedItems)
                if (item.Tag is Guid id) ids.Add(id);
        }
        if (ids.Count == 0) return;

        var idSet = new HashSet<Guid>(ids);
        var updated = 0;
        for (int i = 0; i < _lib.Words.Count; i++)
        {
            var w = _lib.Words[i];
            if (idSet.Contains(w.Id) && w.Enabled != enabled)
            {
                _lib.Words[i] = w with { Enabled = enabled };
                updated++;
            }
        }
        if (updated > 0)
        {
            _lib.UpdatedAt = DateTime.UtcNow;
            Save();
            RefreshWords();
            MessageBox.Show(this, $"已{(enabled ? "启用" : "禁用")} {updated} 条词条", "操作成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void AddCategory()
    {
        var name = InputDialog.Show(this, "请输入分类名称：", "新增分类", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_editor.AddCategory(name))
        {
            MessageBox.Show(this, "分类已存在或名称无效", "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Save();
        RefreshCategories();
    }

    private void RenameCategory()
    {
        if (_lbCategories.SelectedItem is not CategoryItem item || item.IsAll)
        {
            MessageBox.Show(this, "请先选择一个分类", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var oldName = item.Name;
        var newName = InputDialog.Show(this, "请输入新的分类名称：", "重命名分类", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
        _editor.RenameCategory(oldName, newName);
        _lib.UpdatedAt = DateTime.UtcNow;
        Save();
        RefreshCategories();
        RefreshWords();
    }

    private void DeleteCategory()
    {
        if (_lbCategories.SelectedItem is not CategoryItem item || item.IsAll)
        {
            MessageBox.Show(this, "请先选择一个分类", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = item.Name;
        if (MessageBox.Show(this, $"确定删除分类「{name}」？该分类下的词条将移至「未分类」。", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _editor.DeleteCategory(name, "未分类");
        _lib.UpdatedAt = DateTime.UtcNow;
        Save();
        RefreshCategories();
        RefreshWords();
    }

    private void ImportExcel()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            Title = "选择要导入的 Excel 文件",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        DoImport(dlg.FileName, hasHeader: true);
    }

    private void ImportCsv()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "CSV 文件|*.csv",
            Title = "选择要导入的 CSV 文件",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var hasHeader = MessageBox.Show(this, "文件第一行是否包含表头？", "导入选项",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        DoImport(dlg.FileName, hasHeader);
    }

    private void DownloadTemplate()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = "违禁词导入模板.xlsx",
            Title = "保存导入模板",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var generator = new ExcelTemplateGenerator();
            generator.Generate(dlg.FileName);
            if (MessageBox.Show(this, "模板已生成，是否立即打开？", "成功",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dlg.FileName + "\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "生成失败：" + ex.Message, "失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoImport(string fileName, bool hasHeader)
    {
        try
        {
            using var f = new ImportForm(_editor, fileName, hasHeader);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                _lib.UpdatedAt = DateTime.UtcNow;
                Save();
                RefreshCategories();
                RefreshWords();
                UpdateStatus();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "导入失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportExcel()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = "违禁词库.xlsx",
            Title = "导出为 Excel",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var package = new OfficeOpenXml.ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("违禁词库");

            // 表头
            var headers = new[] { "违禁词", "分类", "严重度", "是否启用" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cells[1, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 70, 229));
                cell.Style.Font.Color.SetColor(Color.White);
            }

            // 数据
            var words = _editor.Library.Words;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                var row = i + 2;
                ws.Cells[row, 1].Value = w.Text;
                ws.Cells[row, 2].Value = w.Category;
                ws.Cells[row, 3].Value = w.Severity switch
                {
                    Severity.High => "高",
                    Severity.Medium => "中",
                    _ => "低",
                };
                ws.Cells[row, 4].Value = w.Enabled ? "是" : "否";
            }

            // 列宽
            ws.Column(1).Width = 24;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 12;
            ws.View.FreezePanes(2, 1);

            package.SaveAs(new FileInfo(dlg.FileName));
            MessageBox.Show(this, $"已导出 {words.Count} 条到 Excel", "导出成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Export()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = "wordlib.json",
            Title = "导出词库",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var json = _editor.Export();
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show(this, $"已导出到 {dlg.FileName}", "导出成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SyncToClient()
    {
        var studioDir = Path.GetDirectoryName(_path) ?? AppPaths.BaseDirectory;
        var clientPaths = new[]
        {
            Path.Combine(studioDir, "..", "client", "wordlib.json"),
            Path.Combine(studioDir, "..", "..", "client", "wordlib.json"),
            Path.Combine(studioDir, "wordlib.json"),
        };

        string? target = null;
        foreach (var p in clientPaths)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) { target = full; break; }
        }

        if (target == null)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "JSON 文件|wordlib.json",
                FileName = "wordlib.json",
                Title = "选择客户端目录下的 wordlib.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            target = dlg.FileName;
        }

        try
        {
            Save();
            var json = _lib.ToJson();
            File.WriteAllText(target, json);
            MessageBox.Show(this,
                $"词库已同步到客户端：\n{target}\n\n客户端会自动检测文件变化并重新加载。",
                "同步成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "同步失败：" + ex.Message, "同步失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDeployConfig()
    {
        using var f = new DeployConfigForm(_lib.Metadata);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            _lib.UpdatedAt = DateTime.UtcNow;
            Save();
        }
        RefreshWords();
    }

    private void Save()
    {
        try
        {
            _lib.UpdatedAt = DateTime.UtcNow;
            File.WriteAllText(_path, _lib.ToJson());
            _lastSavedAt = _lib.UpdatedAt;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>GDI+ 圆角矩形扩展方法。</summary>
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
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
