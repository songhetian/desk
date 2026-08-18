using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 客户端入口（组合根）：装配配置 / 词库源 / 悬浮球 / 捕获宿主 / 托盘，启动消息循环。
///
/// 设计约束（需求#6 对齐）：
/// - 客户端纯监听，不含词库管理功能（Studio 是独立软件，仅装管理员机器）
/// - 监控目标 + 告警开关由客户端本地 AppSettings 管理（wordlib.json 只含违禁词数据）
/// - 设置面板可编辑本机部署配置，保存后立即生效
/// </summary>
internal static class Program
{
    private const string SettingsFile = "wordguard.settings.json";
    private const string LibraryFile = "wordlib.json";
    private const string AuditDb = "audit.db";

    [STAThread]
    private static void Main()
    {
        // 全局异常兜底：任何未处理异常都弹窗 + 写日志，避免"无提示直接退出"（此前"打不开"的根因之一）。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, ex) => FatalError(ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            FatalError(ex.ExceptionObject as Exception ?? new Exception("未知非托管异常"));

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            FatalError(ex);
        }
    }

    /// <summary>把致命错误写到 exe 同目录的 startup-error.log，并弹窗告知用户（含可点击下载链接）。</summary>
    private static void FatalError(Exception ex)
    {
        try
        {
            var log = Path.Combine(AppPaths.BaseDirectory, "startup-error.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { /* 日志也写不了就放弃 */ }

        ShowErrorDialog(ex.Message);
        Environment.Exit(1);
    }

    private static void ShowErrorDialog(string message)
    {
        const string downloadUrl = "https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0";

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var form = new Form
        {
            Text = "WordGuard 启动失败",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 520,
            Height = 320,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f),
        };

        // 左侧红色错误图标
        var iconPanel = new Panel
        {
            Left = 0,
            Top = 0,
            Width = 80,
            Height = 260,
            BackColor = Color.FromArgb(254, 226, 226),
        };
        var iconLabel = new Label
        {
            Text = "✕",
            Left = 0,
            Top = 20,
            Width = 80,
            Height = 60,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 32f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 38, 38),
        };
        iconPanel.Controls.Add(iconLabel);

        // 右侧内容区
        var titleLabel = new Label
        {
            Text = "WordGuard 启动失败",
            Left = 100,
            Top = 20,
            Width = 390,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
        };

        var msgLabel = new Label
        {
            Text = message,
            Left = 100,
            Top = 55,
            Width = 390,
            Height = 60,
            ForeColor = Color.FromArgb(75, 85, 99),
        };

        var hintLabel = new Label
        {
            Text = "如提示缺少 .NET 运行时，请点击下方链接下载安装：",
            Left = 100,
            Top = 120,
            Width = 390,
            Height = 25,
            ForeColor = Color.FromArgb(75, 85, 99),
        };

        var linkLabel = new LinkLabel
        {
            Text = downloadUrl,
            Left = 100,
            Top = 145,
            Width = 390,
            Height = 25,
            LinkColor = Color.FromArgb(37, 99, 235),
            ActiveLinkColor = Color.FromArgb(29, 78, 216),
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Microsoft YaHei UI", 9.5f),
        };
        linkLabel.Links.Add(0, downloadUrl.Length, downloadUrl);
        linkLabel.LinkClicked += (_, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Link!.LinkData?.ToString() ?? downloadUrl,
                    UseShellExecute = true,
                });
            }
            catch { }
        };

        var logLabel = new Label
        {
            Text = "详细信息已写入 startup-error.log（位于程序目录）。\n可将该文件发给技术支持协助排查。",
            Left = 100,
            Top = 185,
            Width = 390,
            Height = 45,
            ForeColor = Color.FromArgb(107, 114, 128),
        };

        // 确定按钮
        var okButton = new Button
        {
            Text = "确定",
            Left = 400,
            Top = 240,
            Width = 90,
            Height = 32,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Cursor = Cursors.Hand,
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Click += (_, _) => form.Close();

        form.AcceptButton = okButton;
        form.Controls.AddRange(new Control[]
        {
            iconPanel, titleLabel, msgLabel, hintLabel, linkLabel, logLabel, okButton
        });

        Application.Run(form);
    }

    private static void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 用 exe 真实目录定位配置/词库/审计库，兼容单文件发布（避免指向临时解压目录）。
        var baseDir = AppPaths.BaseDirectory;
        var settingsPath = Path.Combine(baseDir, SettingsFile);
        var settings = AppSettings.Load(settingsPath);
        var libPath = Path.IsPathRooted(settings.WordLibraryPath)
            ? settings.WordLibraryPath
            : Path.Combine(baseDir, settings.WordLibraryPath);
        var auditPath = Path.Combine(baseDir, AuditDb);

        // 首次运行：若词库文件不存在，自动生成示例词库
        EnsureSampleLibrary(libPath);

        // 首次运行：若未配置监控目标，注入常见客服软件作为默认值（需求#6：客户端本地配置）
        if (settings.MonitorTargets.Count == 0)
        {
            settings.MonitorTargets =
            [
                "WeChat.exe",      // 微信
                "QQ.exe",           // QQ
                "Qianniu.exe",      // 千牛
                "Jingmai.exe",      // 京麦
                "Feige.exe",        // 飞鸽客服
                "Douyin.exe",       // 抖店/抖音
            ];
            settings.Save(settingsPath);
        }

        var orb = new OrbStateController(TimeSpan.FromSeconds(3));

        // 审计库初始化（SQLite）：失败绝不能静默崩溃，给出明确上下文后退出。
        AuditLogStore audit;
        try
        {
            audit = new AuditLogStore($"Data Source={auditPath}");
        }
        catch (Exception ex)
        {
            FatalError(new InvalidOperationException(
                $"审计日志数据库初始化失败（SQLite，路径：{auditPath}）：{ex.Message}", ex));
            return;
        }

        // 可变引用：词库热重载后（监控目标等 metadata 变化）重建捕获宿主
        var live = new Live();
        OrbForm orbForm = null!;

        // 已打开的非模态窗体（避免被 GC / using 提前释放）
        var openLogViewer = new System.Collections.Generic.List<LogViewerForm>();

        // ---- 操作回调 ----
        void ShowSettings()
        {
            // 客户端可编辑本机部署配置：传入"获取当前词库源"的委托（rebuild 后词库会被替换）、
            // 配置对象、配置文件路径，以及保存后回调（重新应用覆盖并重建捕获宿主）。
            using var f = new StatusForm(
                () => live.Lib!,
                () => libPath,
                settings,
                settingsPath,
                () => Rebuild(live, libPath, orb, audit, settings, orbForm),
                () => live.Capture?.Stats!,
                () => live.Capture?.DebugLogPath!);
            f.ShowDialog();
        }
        void ShowLog()
        {
            // 非模态窗体：不能用 using var（出作用域立刻 Dispose = 窗体被关），
            // 用应用级列表持有引用，由关闭事件自行移除。
            var f = new LogViewerForm(audit);
            f.FormClosed += (_, _) => openLogViewer.Remove(f);
            openLogViewer.Add(f);
            f.Show();
        }
        void Simulate()
        {
            // 用「真实白名单内的目标名」喂引擎，确保通过目标判定、端到端跑通弹窗/变红/声音/日志。
            // 直接传 demo.exe 会被目标过滤静默丢弃（旧版本"模拟告警测试"无反应的真正原因）。
            var probe = live.Lib?.Metadata.Targets.FirstOrDefault()?.ExeName
                ?? SplitTargets(settings.DebugMonitorTargets).FirstOrDefault()
                ?? "demo.exe";
            // 每次模拟使用唯一 contextId，绕过 CaptureService 的"同文本同上下文去重"，保证点击即触发。
            // skipDedup: true 跳过去重冷却，确保每次点击都能弹出告警（冷却窗口内也不受限）。
            live.Capture?.Feed(
                "这是一段含违禁词的测试内容（绝对化用语、保证包过、百分百最低价）",
                probe, "", $"demo-context-{Guid.NewGuid()}", "模拟窗口",
                skipDedup: true);
        }

        void ExitApp()
        {
            live.Capture?.Dispose();
            audit.Dispose();
            Application.Exit();
        }

        // ---- 悬浮球（主窗体，纯 GDI 绘制，无 WebView2 依赖）----
        orbForm = new OrbForm(orb);
        orbForm.OnOrbClick = ShowSettings;
        orbForm.OnOrbDoubleClick = ShowSettings;
        orbForm.OnExit = ExitApp;
        orbForm.OnShowSettings = ShowSettings;
        orbForm.OnShowLog = ShowLog;
        orbForm.OnSimulate = Simulate;
        orbForm.GetUnacknowledgedCount = () => live.Capture?.Stats.UnacknowledgedAlerts ?? 0;

        // 悬浮球右键菜单
        orbForm.AttachMenu(
            new ToolStripMenuItem("状态面板", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("监控日志", null, (_, _) => ShowLog()),
            new ToolStripMenuItem("模拟告警测试", null, (_, _) => Simulate()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("退出 WordGuard", null, (_, _) => ExitApp())
        );

        // ---- 托盘图标（不含管理端入口）----
        using var tray = new TrayController(ShowSettings, ShowLog, Simulate, ExitApp);

        // ---- 启动监控 ----
        Rebuild(live, libPath, orb, audit, settings, orbForm);
        // 本地调试叠加目标：生产置空，仅在本机验证监控管线时用（不写入词库白名单）
        live.Capture!.ExtraTargetExes = SplitTargets(settings.DebugMonitorTargets);
        live.Capture.OnStatusUpdate = msg => tray.SetStatus(msg);
        orbForm.Show();
        live.Capture.Start();

        // 消息循环（orbForm 是主窗体）
        Application.Run(orbForm);

        // 到这里说明消息循环已结束，做最终清理
        live.Capture?.Dispose();
        orbForm.Dispose();
        audit.Dispose();
    }

    /// <summary>
    /// 首次运行自动生成示例词库（仅违禁词数据，不含部署配置）。
    /// 需求#6：部署配置由客户端 AppSettings 管理，不随 wordlib.json 下发。
    /// </summary>
    private static void EnsureSampleLibrary(string libPath)
    {
        if (File.Exists(libPath))
        {
            // 迁移：旧文件实质为空（0 词）→ 用示例词补齐
            try
            {
                var existing = WordLibrary.LoadFromFile(libPath);
                if (existing.Words.Count == 0)
                {
                    existing.Words.AddRange(SampleWords());
                    existing.UpdatedAt = DateTime.UtcNow;
                    File.WriteAllText(libPath, existing.ToJson());
                }
            }
            catch { /* 损坏文件忽略，等同首次 */ }
            return;
        }

        var sample = new WordLibrary
        {
            UpdatedAt = DateTime.UtcNow,
            Words = SampleWords(),
        };
        var dir = Path.GetDirectoryName(libPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(libPath, sample.ToJson());
    }

    private static List<WordEntry> SampleWords() =>
    [
        new() { Text = "违禁词", Category = "示例", Severity = Severity.High },
        new() { Text = "绝对化用语", Category = "夸大宣传", Severity = Severity.High },
        new() { Text = "最好", Category = "夸大宣传", Severity = Severity.Medium },
        new() { Text = "第一", Category = "夸大宣传", Severity = Severity.Medium },
        new() { Text = "保证", Category = "诱导承诺", Severity = Severity.High },
        new() { Text = "包过", Category = "诱导承诺", Severity = Severity.High },
        new() { Text = "百分百", Category = "夸大宣传", Severity = Severity.Medium },
        new() { Text = "最低价", Category = "价格违规", Severity = Severity.High },
    ];

    private static void Rebuild(Live live, string libPath,
        OrbStateController orb, AuditLogStore audit, AppSettings settings, OrbForm orbForm)
    {
        live.Capture?.Dispose();
        live.Lib?.Dispose();
        var lib = new LibraryFileSource(libPath, TimeSpan.FromSeconds(8), settings.ToMetadata(), watch: true, orb);
        var dispatcher = new AlertDispatcher(lib.Metadata);
        live.Lib = lib;
        live.Capture = new CaptureHost(lib, orb, dispatcher, audit);
        live.Capture.WordHit += (_, _) => orbForm.FlashAlert();
        live.Capture.Start();
    }

    /// <summary>把逗号分隔的目标字符串拆成干净的 EXE 名列表（去空格、去空项）。</summary>
    private static IEnumerable<string> SplitTargets(string raw) =>
        (raw ?? "").Split(',', ';', '\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

    private sealed class Live
    {
        public LibraryFileSource? Lib;
        public CaptureHost? Capture;
    }
}
