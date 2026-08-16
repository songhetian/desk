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
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 运行环境缺失兜底
        if (RuntimeBootstrap.IsMissing())
        {
            Application.Run(new RuntimeMissingForm(".NET 8 运行时",
                "https://dotnet.microsoft.com/download/dotnet/8.0"));
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var settingsPath = Path.Combine(baseDir, SettingsFile);
        var settings = AppSettings.Load(settingsPath);
        var libPath = Path.IsPathRooted(settings.WordLibraryPath)
            ? settings.WordLibraryPath
            : Path.Combine(baseDir, settings.WordLibraryPath);
        var auditPath = Path.Combine(baseDir, AuditDb);

        // 首次运行：若词库文件不存在，自动生成示例词库
        EnsureSampleLibrary(libPath);

        var orb = new OrbStateController(TimeSpan.FromSeconds(3));
        var audit = new AuditLogStore($"Data Source={auditPath}");

        // 可变引用：词库热重载后（监控目标等 metadata 变化）重建捕获宿主
        var live = new Live();
        Rebuild(live, libPath, orb, audit);

        // 已打开的非模态窗体（避免被 GC / using 提前释放）
        var openLogViewer = new System.Collections.Generic.List<LogViewerForm>();

        // ---- 操作回调 ----
        void ShowSettings()
        {
            using var f = new StatusForm(live.Lib!, libPath);
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
        void Simulate() => live.Capture?.Feed(
            "这是一段含违禁词的测试内容", "demo.exe", "", "demo-context", "模拟窗口");

        void ExitApp()
        {
            live.Capture?.Dispose();
            audit.Dispose();
            Application.Exit();
        }

        // ---- 悬浮球（主窗体）----
        using var orbForm = new OrbForm(orb);
        orbForm.OnDoubleClick = ShowSettings;
        orbForm.OnExit = ExitApp;

        // 悬浮球右键菜单（不含管理端入口——Studio 是独立软件）
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
        orbForm.Show();
        live.Capture?.Start();

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
        OrbStateController orb, AuditLogStore audit)
    {
        live.Capture?.Dispose();
        live.Lib?.Dispose();
        var lib = new LibraryFileSource(libPath, TimeSpan.FromSeconds(30), watch: true, orb);
        // 配置锁定：三通道开关等来自词库 metadata（只读）
        var dispatcher = new AlertDispatcher(lib.Metadata);
        live.Lib = lib;
        live.Capture = new CaptureHost(lib, orb, dispatcher, audit);
    }

    private sealed class Live
    {
        public LibraryFileSource? Lib;
        public CaptureHost? Capture;
    }
}

/// <summary>运行环境检测（默认不缺失；环境变量可演示缺失对话框）。</summary>
internal static class RuntimeBootstrap
{
    public static bool IsMissing() =>
        string.Equals(
            Environment.GetEnvironmentVariable("WORDGUARD_SIMULATE_MISSING_RUNTIME"), "1",
            StringComparison.Ordinal);
}
