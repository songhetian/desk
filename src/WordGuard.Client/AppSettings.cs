using System.Text.Json;

namespace WordGuard.Client;

/// <summary>
/// 客户端本地运行配置：仅保存「不可由词库下发的本机运行项」。
///
/// <para>设计约束（grill-me 2026-08-16 对齐）：监控目标 / 三通道开关 / 声音路径 / 去重窗口 / 日志保留
/// 已由管理员在 Studio 锁定、随 <c>wordlib.json</c> 的 metadata 段下发，客户端只读。
/// 因此 AppSettings 不再承载这些「部署配置」，员工无法在本地篡改监控行为。</para>
///
/// <para>保留字段：词库文件路径（员工机器上 wordlib.json 的摆放位置，可由部署脚本决定）；
/// 以及当词库 metadata 缺失时的兜底默认值（保证旧文件 / 缺省环境也能启动）。</para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>本地词库文件路径（wordlib.json），默认与程序同目录。支持相对/UNC 路径。</summary>
    public string WordLibraryPath { get; set; } = "wordlib.json";

    /// <summary>
    /// 兜底：词库 metadata 缺失时的告警去重窗口（秒）。默认 30s。
    /// 正常情况以词库 metadata 为准；仅当词库文件无 metadata 段时使用。
    /// </summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>
    /// 兜底：词库 metadata 缺失时的审计日志本地保留天数。默认 30 天。
    /// 正常情况以词库 metadata 为准。
    /// </summary>
    public int LogRetentionDays { get; set; } = 30;

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
