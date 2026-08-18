using System.Text.Json;
using WordGuard.Client;
using WordGuard.Core;

// 无头冒烟测试：直接驱动真实管线类（词库源→引擎→派遣→审计），关闭所有 UI 通道，
// 验证「检测 / 去重 / 非目标零打扰 / 审计落库 / 离线降级」在真实运行时是否正确。
var dir = Path.Combine(Path.GetTempPath(), "wordguard_smoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var libPath = Path.Combine(dir, "wordlib.json");
var dbPath = Path.Combine(dir, "audit.db");

// 1) 用真实词库模型序列化出一份词库文件
var lib = new WordLibrary
{
    UpdatedAt = DateTime.UtcNow,
    Words =
    [
        new WordEntry { Text = "违禁词", Category = "测试", Severity = Severity.High, Enabled = true },
    ],
};
File.WriteAllText(libPath, JsonSerializer.Serialize(lib, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
}));

// 2) 装配真实组件（watch:false 避免临时文件被监视，确定性更强）
//    需求#6：监控目标 / 三通道开关由客户端本地配置提供（不再从 wordlib.json metadata 读取）
var config = new LibraryMetadata
{
    Targets = { new TargetSpec { ExeName = "notepad.exe" } },
    AlertPopup = false,   // 关闭弹窗通道，避免在无头环境创建 Form
    AlertSound = false,
    AlertHighlight = true,
    CooldownSeconds = 30,
};

var source = new LibraryFileSource(libPath, TimeSpan.FromSeconds(30), config, watch: false);
var orb = new OrbStateController(TimeSpan.FromSeconds(3));
var dispatcher = new AlertDispatcher(source.Metadata);
var audit = new AuditLogStore("Data Source=" + dbPath);

int pass = 0, fail = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
    if (ok) pass++; else fail++;
}

// 复刻 CaptureHost.Feed 的核心管线（不含 UI），返回是否触发了告警
AuditLogEntry? lastEntry = null;
bool Run(string text, string proc)
{
    var result = source.Current.ProcessCapture(new CaptureInput(text, proc, "", "ctx1", DateTime.UtcNow));
    var evt = dispatcher.Dispatch(result);
    if (!evt.HasAlert) return false;
    orb.PulseAlert(DateTime.UtcNow);
    var entry = new AuditLogEntry
    {
        Timestamp = DateTime.UtcNow,
        TargetSoftware = proc,
        WindowTitle = "冒烟窗口",
        TriggeredContent = text,
        MatchedWords = evt.ActiveWords.Select(w => new MatchedWord(w.Id.ToString(), w.Word)).ToList(),
        Severity = evt.TopSeverity,
        Disposition = "已记日志",
        AlertChannels = string.Join(",", evt.Channels.Select(c => c.ToString().ToLowerInvariant())),
    };
    audit.Add(entry);
    lastEntry = entry;
    return true;
}

Console.WriteLine("== WordGuard 无头运行冒烟 ==");

// 3) 命中：监控目标进程 + 含违禁词
var hit = Run("这是一段含违禁词的内容", "notepad.exe");
Check("监控进程命中违禁词 → 触发告警", hit);
Check("审计日志写入 1 行", audit.Count == 1);
Check("命中词记录为「违禁词」", lastEntry?.MatchedWords.Count == 1 && lastEntry.MatchedWords[0].Text == "违禁词");
Check("最高严重度为 High", lastEntry?.Severity == Severity.High);

// 4) 去重：同一上下文 30s 内重复命中应被抑制
var dup = Run("这是一段含违禁词的内容", "notepad.exe");
Check("冷却窗口内重复命中 → 被去重抑制", !dup);
Check("审计日志仍为 1 行（未被刷屏）", audit.Count == 1);

// 5) 非监控目标：零打扰
var off = Run("这是一段含违禁词的内容", "chrome.exe");
Check("非监控进程 → 不触发告警（零打扰）", !off);
Check("审计日志仍为 1 行", audit.Count == 1);

// 6) 离线态：词库文件缺失时 orb 离线、引擎降级为空词库
var offline = new LibraryFileSource(Path.Combine(dir, "missing.json"), TimeSpan.FromSeconds(30), new LibraryMetadata(), watch: false);
Check("词库缺失 → Status.FileExists 为 false（悬浮球离线）", !offline.Status.FileExists);
var safe = offline.Current.ProcessCapture(new CaptureInput("含违禁词", "notepad.exe", "", "c", DateTime.UtcNow));
Check("词库缺失 → 引擎降级为空词库（不崩溃）", !dispatcher.Dispatch(safe).HasAlert);

Console.WriteLine($"\n结果：{pass} 通过 / {fail} 失败");
audit.Dispose();
offline.Dispose();
try { Directory.Delete(dir, true); } catch { /* 最佳努力清理 */ }
Environment.Exit(fail == 0 ? 0 : 1);
