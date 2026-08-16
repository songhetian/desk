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
}
