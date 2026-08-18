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
    // 注意：Categories/WordEntry/TargetSpec/LibraryMetadata 等数据类放在本文件末尾
    /// <summary>当前程序能够理解的最高 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>词库格式版本，用于新旧客户端之间的兼容判断。</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>词库最后更新时间（UTC）。由管理端导出时写入，供客户端/部署确认分发是否生效（PRD 数据契约 <c>updatedAt</c>）。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.MinValue;

    /// <summary>违禁词条目列表。</summary>
    public List<WordEntry> Words { get; set; } = new();

    /// <summary>
    /// 分类列表（独立维护，允许空分类存在）。旧词库无此字段时由词条动态推导并补齐。
    /// </summary>
    public List<CategorySpec> Categories { get; set; } = new();

    /// <summary>
    /// 部署配置元数据段（监控目标 / 三通道开关 / 声音路径 / 去重 / 保留）。
    /// 需求#6：部署配置由客户端本地 AppSettings 管理，不再随 wordlib.json 下发。
    /// [JsonIgnore] 确保导出时不含此字段；旧文件中的 metadata 段在反序列化时自动忽略。
    /// </summary>
    [JsonIgnore]
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

    /// <summary>默认重试次数（文件被占用时的重试次数）。</summary>
    public const int DefaultMaxRetries = 3;
    /// <summary>默认重试间隔（毫秒）。</summary>
    public const int DefaultRetryDelayMs = 50;

    /// <summary>
    /// 从文件加载词库。文件不存在时返回<b>空词库</b>（不抛异常），
    /// 使监控在词库缺失时继续运行（仅不匹配任何词），而非崩溃。缺失判定由调用方用于离线指示。
    /// 遇到文件被占用（IOException）时自动重试，重试失败后降级为空词库。
    /// </summary>
    public static WordLibrary LoadFromFile(string path)
        => LoadFromFile(path, DefaultMaxRetries, DefaultRetryDelayMs);

    /// <summary>
    /// 从文件加载词库，可配置重试参数。
    /// </summary>
    public static WordLibrary LoadFromFile(string path, int maxRetries, int retryDelayMs)
    {
        if (!File.Exists(path))
            return new WordLibrary();

        var retries = Math.Max(0, maxRetries);
        for (int i = 0; i <= retries; i++)
        {
            try
            {
                return Load(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                // 文件损坏：同样降级为空词库，保证监控不中断
                return new WordLibrary();
            }
            catch (IOException)
            {
                // 文件被占用（正在写入中）：重试
                if (i < retries)
                    System.Threading.Thread.Sleep(retryDelayMs);
                else
                    return new WordLibrary(); // 重试用尽：降级为空
            }
        }
        return new WordLibrary();
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

    /// <summary>语音播报开关（默认开）。命中违禁词时朗读告警文案。</summary>
    public bool AlertVoice { get; set; } = true;

    /// <summary>检测到违禁词后自动删除输入内容（Ctrl+A + Backspace），默认关。</summary>
    public bool AutoDelete { get; set; } = false;

    /// <summary>自定义告警声音文件路径（可选）。留空则使用系统默认提示音。可相对词库文件所在目录。</summary>
    public string SoundFilePath { get; set; } = "";

    /// <summary>告警去重窗口（秒）。默认 30s。</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>审计日志本地保留天数。默认 30 天。</summary>
    public int LogRetentionDays { get; set; } = 30;
}

/// <summary>违禁词分类定义（独立持久化，允许空分类）。</summary>
public sealed class CategorySpec
{
    /// <summary>分类名称（唯一标识）。</summary>
    public string Name { get; set; } = "";

    /// <summary>分类说明（可选）。</summary>
    public string Description { get; set; } = "";
}
