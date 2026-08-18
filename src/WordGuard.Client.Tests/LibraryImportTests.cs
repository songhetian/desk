using System.IO;
using WordGuard.Client;
using WordGuard.Core;
using Xunit;

namespace WordGuard.Client.Tests;

/// <summary>
/// 客户端导入校验：聚焦"数据完整、准确、可被本客户端理解"，异常给出明确错误（需求#5）。
/// 纯逻辑、与 UI 解耦，便于单测。
/// </summary>
public class LibraryImportTests
{
    private static string ValidJson()
    {
        var lib = new WordLibrary();
        lib.Words.Add(new WordEntry { Text = "绝对化用语", Category = "夸大宣传", Severity = Severity.High });
        lib.Words.Add(new WordEntry { Text = "包过", Category = "诱导承诺", Severity = Severity.High });
        return lib.ToJson();
    }

    [Fact]
    public void Validate_valid_library_succeeds_and_counts_words()
    {
        var r = new ClientLibraryImporter().Validate(ValidJson());

        Assert.True(r.Success);
        Assert.Equal(2, r.WordCount);
        Assert.NotNull(r.Library);
        Assert.Equal("绝对化用语", r.Library!.Words[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_empty_content_fails(string? json)
    {
        var r = new ClientLibraryImporter().Validate(json!);

        Assert.False(r.Success);
        Assert.Contains("空", r.Message);
    }

    [Fact]
    public void Validate_malformed_json_fails()
    {
        var r = new ClientLibraryImporter().Validate("{not valid json");

        Assert.False(r.Success);
        Assert.Contains("JSON", r.Message);
    }

    [Fact]
    public void Validate_library_without_words_fails()
    {
        var r = new ClientLibraryImporter().Validate("{\"schemaVersion\":1,\"words\":[]}");

        Assert.False(r.Success);
        Assert.Contains("违禁词", r.Message);
    }

    [Fact]
    public void Validate_schema_version_too_new_fails()
    {
        var r = new ClientLibraryImporter().Validate("{\"schemaVersion\":99,\"words\":[{\"text\":\"x\"}]}");

        Assert.False(r.Success);
        Assert.True(r.TooNewSchema);
        Assert.Contains("版本", r.Message);
    }

    [Fact]
    public void Validate_library_with_blank_word_text_fails()
    {
        var r = new ClientLibraryImporter().Validate(
            "{\"schemaVersion\":1,\"words\":[{\"text\":\"   \",\"severity\":\"high\"}]}");

        Assert.False(r.Success);
        Assert.Contains("空白", r.Message);
    }

    [Fact]
    public void Import_missing_source_file_fails_and_does_not_write_dest()
    {
        var dest = Path.Combine(Path.GetTempPath(), "wg_import_test_" + System.Guid.NewGuid() + ".json");
        try
        {
            var r = new ClientLibraryImporter().Import(
                Path.Combine(Path.GetTempPath(), "nope_" + System.Guid.NewGuid() + ".json"), dest);

            Assert.False(r.Success);
            Assert.Contains("不存在", r.Message);
            Assert.False(File.Exists(dest));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public void Import_valid_writes_dest_and_is_reloadable()
    {
        var src = Path.Combine(Path.GetTempPath(), "wg_src_" + System.Guid.NewGuid() + ".json");
        var dest = Path.Combine(Path.GetTempPath(), "wg_dest_" + System.Guid.NewGuid() + ".json");
        try
        {
            var lib = new WordLibrary();
            lib.Words.Add(new WordEntry { Text = "导入词", Severity = Severity.Medium });
            File.WriteAllText(src, lib.ToJson());

            var r = new ClientLibraryImporter().Import(src, dest);

            Assert.True(r.Success);
            Assert.True(File.Exists(dest));
            var reloaded = WordLibrary.LoadFromFile(dest);
            Assert.Equal(1, reloaded.Words.Count);
            Assert.Equal("导入词", reloaded.Words[0].Text);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public void Import_invalid_does_not_write_dest()
    {
        var src = Path.Combine(Path.GetTempPath(), "wg_bad_" + System.Guid.NewGuid() + ".json");
        var dest = Path.Combine(Path.GetTempPath(), "wg_bad_dest_" + System.Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(src, "{bad json");
            var r = new ClientLibraryImporter().Import(src, dest);

            Assert.False(r.Success);
            Assert.False(File.Exists(dest));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public void ImportJson_overwrite_mode_replaces_existing_library()
    {
        var dest = Path.Combine(Path.GetTempPath(), "wg_ovw_" + System.Guid.NewGuid() + ".json");
        try
        {
            var existing = new WordLibrary();
            existing.Words.Add(new WordEntry { Text = "旧词", Severity = Severity.Low });
            File.WriteAllText(dest, existing.ToJson());

            var newLib = new WordLibrary();
            newLib.Words.Add(new WordEntry { Text = "新词", Severity = Severity.High });

            var r = new ClientLibraryImporter().ImportJson(newLib.ToJson(), dest, ImportMode.Overwrite);

            Assert.True(r.Success);
            var reloaded = WordLibrary.LoadFromFile(dest);
            Assert.Equal(1, reloaded.Words.Count);
            Assert.Equal("新词", reloaded.Words[0].Text);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public void ImportJson_append_mode_adds_new_words_and_keeps_existing()
    {
        var dest = Path.Combine(Path.GetTempPath(), "wg_app_" + System.Guid.NewGuid() + ".json");
        try
        {
            var existing = new WordLibrary();
            existing.Words.Add(new WordEntry { Text = "原有词", Severity = Severity.Low });
            File.WriteAllText(dest, existing.ToJson());

            var newLib = new WordLibrary();
            newLib.Words.Add(new WordEntry { Text = "新增词", Severity = Severity.High });

            var r = new ClientLibraryImporter().ImportJson(newLib.ToJson(), dest, ImportMode.Append);

            Assert.True(r.Success);
            var reloaded = WordLibrary.LoadFromFile(dest);
            Assert.Equal(2, reloaded.Words.Count);
            Assert.Contains(reloaded.Words, w => w.Text == "原有词");
            Assert.Contains(reloaded.Words, w => w.Text == "新增词");
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public void ImportJson_append_mode_skips_duplicate_words()
    {
        var dest = Path.Combine(Path.GetTempPath(), "wg_dup_" + System.Guid.NewGuid() + ".json");
        try
        {
            var existing = new WordLibrary();
            existing.Words.Add(new WordEntry { Text = "重复词", Category = "旧分类", Severity = Severity.Low });
            File.WriteAllText(dest, existing.ToJson());

            var newLib = new WordLibrary();
            newLib.Words.Add(new WordEntry { Text = "重复词", Category = "新分类", Severity = Severity.High });
            newLib.Words.Add(new WordEntry { Text = "新词", Severity = Severity.Medium });

            var r = new ClientLibraryImporter().ImportJson(newLib.ToJson(), dest, ImportMode.Append);

            Assert.True(r.Success);
            var reloaded = WordLibrary.LoadFromFile(dest);
            Assert.Equal(2, reloaded.Words.Count);
            var dup = reloaded.Words.First(w => w.Text == "重复词");
            Assert.Equal("旧分类", dup.Category);
            Assert.Equal(Severity.Low, dup.Severity);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public void ImportJson_append_mode_works_when_dest_does_not_exist()
    {
        var dest = Path.Combine(Path.GetTempPath(), "wg_new_" + System.Guid.NewGuid() + ".json");
        try
        {
            var newLib = new WordLibrary();
            newLib.Words.Add(new WordEntry { Text = "新词", Severity = Severity.High });

            var r = new ClientLibraryImporter().ImportJson(newLib.ToJson(), dest, ImportMode.Append);

            Assert.True(r.Success);
            Assert.True(File.Exists(dest));
            var reloaded = WordLibrary.LoadFromFile(dest);
            Assert.Equal(1, reloaded.Words.Count);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }
}
