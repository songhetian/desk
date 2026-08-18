using System;
using System.IO;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Core.Tests;

public class WordLibraryTests
{
    [Fact]
    public void Round_trips_words_with_metadata()
    {
        var lib = new WordLibrary
        {
            Words =
            {
                new WordEntry { Text = "绝对", Category = "夸大宣传", Severity = Severity.High, Enabled = true },
                new WordEntry { Text = "免费送", Category = "诱导", Severity = Severity.Medium, Enabled = false },
            }
        };

        var json = lib.ToJson();
        var back = WordLibrary.Load(json);

        Assert.Equal(2, back.Words.Count);
        Assert.Equal("绝对", back.Words[0].Text);
        Assert.Equal(Severity.High, back.Words[0].Severity);
        Assert.Equal("诱导", back.Words[1].Category);
        Assert.False(back.Words[1].Enabled);
        Assert.Equal(WordLibrary.CurrentSchemaVersion, back.SchemaVersion);
    }

    [Fact]
    public void Ignores_unknown_fields_and_flags_newer_schema()
    {
        // 高版本词库带未来字段；旧客户端应忽略未知字段继续工作，并标记"需升级"
        const string json = """
        {
          "schemaVersion": 99,
          "words": [ { "text": "测试", "category": "x", "severity": "low", "enabled": true, "futureField": true } ],
          "someNewTopLevel": "ignored"
        }
        """;

        var lib = WordLibrary.Load(json);

        Assert.True(lib.NewerSchemaDetected);
        Assert.Single(lib.Words);
        Assert.Equal("测试", lib.Words[0].Text);
        Assert.Equal(Severity.Low, lib.Words[0].Severity);
    }

    [Fact]
    public void Empty_input_yields_empty_library()
    {
        var lib = WordLibrary.Load("");

        Assert.NotNull(lib);
        Assert.Empty(lib.Words);
    }

    /// <summary>需求#6：管理端只导出违禁词数据，部署配置（metadata 段）不随 wordlib.json 下发。</summary>
    [Fact]
    public void Export_excludes_metadata_from_json()
    {
        var lib = new WordLibrary
        {
            Words = { new WordEntry { Text = "退货", Enabled = true, Severity = Severity.High } },
            Metadata = new LibraryMetadata
            {
                Targets = [new TargetSpec { ExeName = "WeChat.exe" }],
                AlertPopup = false,
            },
        };

        var json = lib.ToJson();

        // 导出的 JSON 不应包含 metadata 段
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
        // 但应包含 words 数据
        Assert.Contains("words", json, StringComparison.OrdinalIgnoreCase);
        // 回读后 Metadata 应为默认值（不从 JSON 恢复）
        var back = WordLibrary.Load(json);
        Assert.Empty(back.Metadata.Targets);
        Assert.True(back.Metadata.AlertPopup); // 默认值
    }

    [Fact]
    public void File_locked_by_another_process_falls_back_gracefully()
    {
        // 模拟文件被占用（管理端正在写入）：不应抛异常，降级为空词库
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}");

        try
        {
            // 以独占方式打开文件，模拟"正在写入中"
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            // LoadFromFile 遇到文件被占用（IOException）应降级为空，不崩溃
            var lib = WordLibrary.LoadFromFile(path);
            Assert.NotNull(lib);
            Assert.Empty(lib.Words); // 读不到 → 空词库
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Retry_eventually_succeeds_when_file_becomes_available()
    {
        // 验证重试机制：第一次读失败，重试时成功
        // 用一个临时文件 + 手动控制的"释放锁"来验证
        var dir = Path.Combine(Path.GetTempPath(), "wg_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wordlib.json");
        const string validJson = "{\"schemaVersion\":1,\"words\":[{\"text\":\"退货\",\"enabled\":true,\"severity\":\"high\"}]}";
        File.WriteAllText(path, validJson);

        try
        {
            // 先把文件锁住
            var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            // 启动一个后台任务：一会儿后释放锁
            var releaseTask = System.Threading.Tasks.Task.Delay(80).ContinueWith(_ =>
            {
                lockStream.Dispose();
            });

            // LoadFromFile 应该会重试，等锁释放后读到正确内容
            var lib = WordLibrary.LoadFromFile(path, maxRetries: 5, retryDelayMs: 50);

            releaseTask.Wait();

            // 应该读到了有效内容（而不是空词库）
            Assert.Single(lib.Words);
            Assert.Equal("退货", lib.Words[0].Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
