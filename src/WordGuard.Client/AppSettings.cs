using System.Collections.Generic;
using System.Text.Json;
using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>
/// 客户端本地运行配置。
///
/// <para>本机部署配置：监控目标 / 三通道开关 / 去重窗口 / 日志保留，管理员可在 Studio 随
/// <c>wordlib.json</c> 下发默认值，但<b>本端可在「监控状态」面板中覆盖并保存</b>（<see cref="Deployment"/>）。
/// 覆盖段为空（null）时沿用下发默认值；一旦本端保存，则以本端为准、立即生效。</para>
///
/// <para>保留字段：词库文件路径（员工机器上 wordlib.json 的摆放位置，可由部署脚本决定）；
/// 以及本地调试监控目标（仅开发/自测用，生产应留空）。</para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>本地词库文件路径（wordlib.json），默认与程序同目录。支持相对/UNC 路径。</summary>
    public string WordLibraryPath { get; set; } = "wordlib.json";

    /// <summary>
    /// 本地调试监控目标（仅开发/自测用，生产应留空）：逗号分隔的进程 EXE 名，如 <c>"notepad.exe,chrome.exe"</c>。
    /// 这些目标<b>不写入</b>管理员下发的 wordlib.json，仅在本机叠加生效，方便验证监控管线。
    /// </summary>
    public string DebugMonitorTargets { get; set; } = "";

    /// <summary>
    /// 本机部署配置覆盖（客户端可编辑）。为 null 时表示「沿用管理端下发的默认值」；
    /// 一旦在客户端「监控状态」面板保存，即以本端配置为准、立即生效。
    /// </summary>
    public ClientDeployment? Deployment { get; set; }

    /// <summary>
    /// 兜底：词库 metadata 缺失时的告警去重窗口（秒）。默认 30s。（正常情况以生效配置为准。）
    /// </summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>
    /// 兜底：词库 metadata 缺失时的审计日志本地保留天数。默认 30 天。（正常情况以生效配置为准。）
    /// </summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>
    /// 把本机覆盖配置应用到生效的部署元数据上：覆盖段非空的项逐项覆盖默认值。
    /// 用于让客户端保存的配置真正影响监控目标与告警行为。
    /// </summary>
    public static void ApplyOverrides(LibraryMetadata m, AppSettings settings)
    {
        var d = settings.Deployment;
        if (d is null) return;
        if (d.MonitorTargets.Count > 0)
            m.Targets = d.MonitorTargets
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Select(t => new TargetSpec { ExeName = t })
                .ToList();
        if (d.AlertPopup.HasValue) m.AlertPopup = d.AlertPopup.Value;
        if (d.AlertSound.HasValue) m.AlertSound = d.AlertSound.Value;
        if (d.AlertHighlight.HasValue) m.AlertHighlight = d.AlertHighlight.Value;
        if (d.CooldownSeconds.HasValue) m.CooldownSeconds = d.CooldownSeconds.Value;
        if (d.LogRetentionDays.HasValue) m.LogRetentionDays = d.LogRetentionDays.Value;
    }

    /// <summary>从文件加载配置。文件不存在或内容为空/损坏时返回<b>默认值</b>（不抛异常），使客户端总能启动。</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new AppSettings();
            return FromJson(json);
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>从 JSON 文本反序列化；未知字段默认忽略（不抛异常）。</summary>
    public static AppSettings FromJson(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions()) ?? new AppSettings();

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>序列化为带缩进的 JSON 文本（便于人工核对与备份）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions());

    /// <summary>保存到指定文件（原子性：先写临时文件再替换，避免半截文件）。</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// 本机部署配置覆盖段（客户端「监控状态」面板可编辑）。所有字段均为可空：
/// 为 null 表示该顶「沿用管理端下发的默认值」；一旦本端填写并保存，即以本端值为准。
/// </summary>
public sealed class ClientDeployment
{
    /// <summary>监控目标进程名列表（每行/每项一个 EXE 名，如 "WeChat.exe"）。空列表=沿用下发。</summary>
    public List<string> MonitorTargets { get; set; } = new();

    /// <summary>弹窗提醒开关（null=沿用下发）。</summary>
    public bool? AlertPopup { get; set; }

    /// <summary>声音提醒开关（null=沿用下发）。</summary>
    public bool? AlertSound { get; set; }

    /// <summary>自有界面高亮开关（null=沿用下发）。</summary>
    public bool? AlertHighlight { get; set; }

    /// <summary>告警去重窗口（秒，null=沿用下发）。</summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>审计日志本地保留天数（null=沿用下发）。</summary>
    public int? LogRetentionDays { get; set; }
}
