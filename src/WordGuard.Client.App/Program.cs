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
/// 设计约束（grill-me 2026-08-16 对齐）：
/// - 客户端纯监听，不含词库管理功能（Studio 是独立软件，仅装管理员机器）
/// - 监控目标 + 告警开关由管理员锁定（随 wordlib.json 下发），员工不可改
/// - 设置面板为只读状态展示
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

    /// <summary>把致命错误写到 exe 同目录的 startup-error.log，并弹窗告知用户（含细节）。</summary>
    private static void FatalError(Exception ex)
    {
        try
        {
            var log = Path.Combine(AppPaths.BaseDirectory, "startup-error.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { /* 日志也写不了就放弃 */ }

        MessageBox.Show(
            null,
            "WordGuard 启动失败：\n" + ex.Message + "\n\n详细信息已写入 startup-error.log（位于程序目录）。\n可将该文件发给技术支持协助排查。",
            "WordGuard 启动失败",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        Environment.Exit(1);
    }

    private static void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 运行环境缺失兜底：自包含包已含 .NET 运行时，真正可能缺的是 WebView2 运行时（系统级组件）。
        // 早期真实探测，缺则引导安装，避免后续 WebView2 窗体静默崩溃（此前"打不开也无提示"的根因）。
        if (!WebRuntime.IsWebView2Available())
        {
            Application.Run(new RuntimeMissingForm("Microsoft Edge WebView2 运行时",
                "https://developer.microsoft.com/zh-cn/microsoft-edge/webview2/"));
            return;
        }

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
        Rebuild(live, libPath, orb, audit, settings);

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
                () => Rebuild(live, libPath, orb, audit, settings));
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
            live.Capture?.Feed(
                "这是一段含违禁词的测试内容（绝对化用语、保证包过、百分百最低价）",
                probe, "", "demo-context", "模拟窗口");
        }

        void ExitApp()
        {
            live.Capture?.Dispose();
            audit.Dispose();
            Application.Exit();
        }

        // ---- 悬浮球（主窗体，WebView2 渲染 orb.html，像素级还原设计稿）----
        using var orbForm = new OrbWebViewForm(orb);
        orbForm.OnDoubleClick = ShowSettings;
        orbForm.OnExit = ExitApp;
        orbForm.OnShowSettings = ShowSettings;
        orbForm.OnShowLog = ShowLog;
        orbForm.OnSimulate = Simulate;

        // 悬浮球右键菜单（不含管理端入口——Studio 是独立软件）。
        // WebView2 正常时用现代化 HTML 弹层；此处 WinForms 菜单仅作为 GDI 降级（WebView2 不可用）时的兜底。
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
        // 本地调试叠加目标：生产置空，仅在本机验证监控管线时用（不写入词库白名单）
        live.Capture!.ExtraTargetExes = SplitTargets(settings.DebugMonitorTargets);
        live.Capture.OnStatusUpdate = msg => tray.SetStatus(msg);
        orbForm.Show();
        live.Capture.Start();

        // 消息循环（orbForm 是主窗体）
        Application.Run(orbForm);

        // 到这里说明消息循环已结束，做最终清理
        live.Capture?.Dispose();
        audit.Dispose();
    }

    /// <summary>
    /// 首次运行自动生成示例词库（含默认监控目标，便于开箱即监控）。
    /// 同时对「已存在但实质为空且无 metadata」的旧 wordlib.json 做就地迁移（用默认 metadata 补齐，
    /// 保证监控开箱即能命中 WeChat / QQ），不覆盖用户已编辑的词条。
    /// </summary>
    private static void EnsureSampleLibrary(string libPath)
    {
        if (File.Exists(libPath))
        {
            // 迁移：旧文件实质为空（0 词 + 无 metadata）→ 用默认 metadata + 示例词补齐
            try
            {
                var existing = WordLibrary.LoadFromFile(libPath);
                if (existing.Words.Count == 0 && existing.Metadata.Targets.Count == 0)
                {
                    existing.Words.AddRange(SampleWords());
                    existing.Metadata.Targets.AddRange(SampleMetadata().Targets);
                    existing.Metadata.AlertPopup = true;
                    existing.Metadata.AlertSound = true;
                    existing.Metadata.AlertHighlight = true;
                    existing.Metadata.CooldownSeconds = 30;
                    existing.Metadata.LogRetentionDays = 30;
                    existing.UpdatedAt = DateTime.UtcNow;
                    File.WriteAllText(libPath, JsonSerializer.Serialize(existing, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }));
                }
            }
            catch { /* 损坏文件忽略，等同首次 } */}
            return;
        }

        var sample = new WordLibrary
        {
            UpdatedAt = DateTime.UtcNow,
            Words = SampleWords(),
            Metadata = SampleMetadata(),
        };
        var dir = Path.GetDirectoryName(libPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(libPath, JsonSerializer.Serialize(sample, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
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

    private static LibraryMetadata SampleMetadata() => new()
    {
        // 默认监控常见客服软件；管理员应在 Studio 中按实际环境修改后重新分发
        Targets =
        [
            new TargetSpec { ExeName = "WeChat.exe" },
            new TargetSpec { ExeName = "QQ.exe" },
        ],
        AlertPopup = true,
        AlertSound = true,
        AlertHighlight = true,
        CooldownSeconds = 30,
        LogRetentionDays = 30,
    };

    private static void Rebuild(Live live, string libPath,
        OrbStateController orb, AuditLogStore audit, AppSettings settings)
    {
        live.Capture?.Dispose();
        live.Lib?.Dispose();
        var lib = new LibraryFileSource(libPath, TimeSpan.FromSeconds(30), watch: true, orb);
        // 管理端下发的 metadata 为默认值；本端若保存了部署配置覆盖，则逐项覆盖（客户端可改）。
        AppSettings.ApplyOverrides(lib.Metadata, settings);
        var dispatcher = new AlertDispatcher(lib.Metadata);
        live.Lib = lib;
        live.Capture = new CaptureHost(lib, orb, dispatcher, audit);
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
