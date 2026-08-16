using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordGuard.Core;

/// <summary>
/// 违禁词库：内存模型 + JSON 契约。
/// 磁盘格式 human-readable（小驼峰），未知字段默认忽略，从而兼容更高版本的词库文件。
/// </summary>
public sealed class WordLibrary
{
    /// <summary>当前程序能够理解的最高 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>词库格式版本，用于新旧客户端之间的兼容判断。</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>词库最后更新时间（UTC）。由管理端导出时写入，供客户端/部署确认分发是否生效（PRD 数据契约 <c>updatedAt</c>）。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.MinValue;

    /// <summary>违禁词条目列表。</summary>
    public List<WordEntry> Words { get; set; } = new();

    /// <summary>
    /// 部署配置元数据段（监控目标 / 三通道开关 / 声音路径 / 去重 / 保留）。
    /// 由管理员在 Studio 配置并随词库下发，客户端只读；老文件缺省时返回默认值。
    /// </summary>
    public LibraryMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 加载时若发现文件 <c>schemaVersion</c> 高于本程序能理解的版本，则为 true。
    /// 调用方应据此在状态栏提示"词库需升级"。
    /// </summary>
    public bool NewerSchemaDetected { get; private set; }

    /// <summary>
    /// 从 JSON 文本加载词库。空或空白输入返回空词库；未知字段自动忽略（不抛异常）。
    /// </summary>
    public static WordLibrary Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WordLibrary();

        var lib = JsonSerializer.Deserialize<WordLibrary>(json, JsonOptions())
                 ?? new WordLibrary();

        if (lib.SchemaVersion > CurrentSchemaVersion)
            lib.NewerSchemaDetected = true;

        return lib;
    }

    /// <summary>序列化为带缩进的 JSON 文本，便于人工备份与共享目录交换。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions());

    /// <summary>
    /// 从文件加载词库。文件不存在时返回<b>空词库</b>（不抛异常），
    /// 使监控在词库缺失时继续运行（仅不匹配任何词），而非崩溃。缺失判定由调用方用于离线指示。
    /// </summary>
    public static WordLibrary LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new WordLibrary();
        try
        {
            return Load(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            // 文件损坏：同样降级为空词库，保证监控不中断
            return new WordLibrary();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 枚举以 lowercase 字符串读写（"high"/"medium"/"low"），对齐词库契约
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// 一个被监控目标软件的标识：按 EXE 名匹配，可附加可选路径做更精确约束。
/// 由管理员在 Studio 配置，随 <see cref="WordLibrary"/> 的 metadata 段下发，客户端只读。
/// </summary>
public sealed class TargetSpec
{
    /// <summary>进程 EXE 名（如 "WeChat.exe"），大小写不敏感匹配。</summary>
    public string ExeName { get; set; } = "";

    /// <summary>
    /// 可选：进程完整路径前缀（如 @"C:\Apps\WeChat\WeChat.exe" 或目录 @"C:\Apps\WeChat\"）。
    /// 留空表示只按 EXE 名匹配；填写后需「EXE 名命中 且 路径前缀命中」才算被监控，
    /// 用于区分同名 EXE 的不同安装（PRD：目标匹配增强）。
    /// </summary>
    public string? ExePath { get; set; }
}

/// <summary>
/// 词库随附的「部署配置」段。这些项由管理员在 Studio 锁定、随词库文件下发，
/// 客户端读取后只读（员工无法在 setting 面板修改）。
/// 包含：监控目标列表、三通道开关、告警声音路径、去重窗口、日志保留天数。
/// </summary>
public sealed class LibraryMetadata
{
    /// <summary>被监控的目标软件列表（EXE 名 + 可选路径）。</summary>
    public List<TargetSpec> Targets { get; set; } = new();

    /// <summary>弹窗告警开关（默认开）。</summary>
    public bool AlertPopup { get; set; } = true;

    /// <summary>声音告警开关（默认开）。</summary>
    public bool AlertSound { get; set; } = true;

    /// <summary>自有界面高亮开关（默认开）。</summary>
    public bool AlertHighlight { get; set; } = true;

    /// <summary>自定义告警声音文件路径（可选）。留空则使用系统默认提示音。可相对词库文件所在目录。</summary>
    public string SoundFilePath { get; set; } = "";

    /// <summary>告警去重窗口（秒）。默认 30s。</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>审计日志本地保留天数。默认 30 天。</summary>
    public int LogRetentionDays { get; set; } = 30;
}
