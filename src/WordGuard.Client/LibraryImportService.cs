using System.IO;
using System.Text.Json;
using WordGuard.Core;

namespace WordGuard.Client;

/// <summary>客户端导入违禁词库的结果。</summary>
public sealed record LibraryImportResult
{
    /// <summary>是否可安全导入。</summary>
    public bool Success { get; init; }

    /// <summary>面向用户的中文提示（成功或失败原因）。</summary>
    public string Message { get; init; } = "";

    /// <summary>校验通过时的词库实例（null=校验失败）。</summary>
    public WordLibrary? Library { get; init; }

    /// <summary>校验通过时的违禁词数量。</summary>
    public int WordCount { get; init; }

    /// <summary>是否因词库版本过高而被拒绝（提示用户升级客户端）。</summary>
    public bool TooNewSchema { get; init; }
}

/// <summary>
/// 客户端导入违禁词库的校验与落盘。
///
/// <para>校验聚焦"数据完整、准确、可被本客户端理解"（需求#5）：空内容 / 非法 JSON / 无违禁词 /
/// 版本过高 / 空白词条 都给出明确中文错误，避免"导入失败却无提示"。</para>
///
/// <para>落盘采用原子写入（临时文件 + 替换），避免半截文件导致监控引擎崩溃；
/// 写入成功后由现有 <see cref="LibraryFileSource"/> 的 FileSystemWatcher 自动热重载。</para>
/// </summary>
public sealed class ClientLibraryImporter
{
    /// <summary>校验词库 JSON 文本是否可安全导入。不写盘。</summary>
    public LibraryImportResult Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("词库文件内容为空，无法导入");

        WordLibrary lib;
        try
        {
            lib = WordLibrary.Load(json);
        }
        catch (JsonException)
        {
            return Fail("文件不是合法的 JSON 格式，无法解析");
        }

        if (lib.NewerSchemaDetected || lib.SchemaVersion > WordLibrary.CurrentSchemaVersion)
            return new LibraryImportResult
            {
                Success = false,
                TooNewSchema = true,
                Message = $"词库版本（v{lib.SchemaVersion}）高于本客户端支持的最高版本" +
                          $"（v{WordLibrary.CurrentSchemaVersion}），请升级客户端后再导入",
            };

        if (lib.Words.Any(w => string.IsNullOrWhiteSpace(w.Text)))
            return Fail("词库中存在空白的违禁词文本，数据不完整，已拒绝导入");

        if (lib.Words.Count == 0)
            return Fail("词库不包含任何违禁词，无法导入");

        return new LibraryImportResult
        {
            Success = true,
            Message = "校验通过",
            Library = lib,
            WordCount = lib.Words.Count,
        };
    }

    /// <summary>从源文件导入词库到目标路径：先校验源文件，再原子写入目标（触发客户端热重载）。</summary>
    public LibraryImportResult Import(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath))
            return Fail("词库文件不存在，无法导入");

        string json;
        try
        {
            json = File.ReadAllText(sourcePath);
        }
        catch (IOException ex)
        {
            return Fail($"无法读取词库文件：{ex.Message}");
        }

        return ImportJson(json, destPath);
    }

    /// <summary>从已读取的 JSON 文本导入到目标路径（浏览器端选择文件后直接传内容时用）。</summary>
    public LibraryImportResult ImportJson(string json, string destPath)
    {
        var validation = Validate(json);
        if (!validation.Success)
            return validation;

        try
        {
            WriteAtomic(json, destPath);
        }
        catch (IOException ex)
        {
            return Fail($"写入词库失败：{ex.Message}");
        }

        return validation with { Message = $"已导入 {validation.WordCount} 条违禁词 ✓" };
    }

    /// <summary>原子写入：临时文件 + 替换，避免半截文件导致监控引擎崩溃。</summary>
    private static void WriteAtomic(string json, string destPath)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = destPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, destPath, overwrite: true);
    }

    private static LibraryImportResult Fail(string message) =>
        new() { Success = false, Message = message };
}
