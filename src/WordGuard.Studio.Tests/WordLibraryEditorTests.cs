using WordGuard.Core;
using WordGuard.Studio;
using Xunit;

namespace WordGuard.Studio.Tests;

public class WordLibraryEditorTests
{
    [Fact]
    public void Add_valid_word_succeeds_and_appends()
    {
        var lib = new WordLibrary();
        var editor = new WordLibraryEditor(lib);

        var result = editor.Add(new WordEntry { Text = "绝对化用语", Category = "夸大宣传", Severity = Severity.High });

        Assert.Equal(AddWordResult.Success, result);
        Assert.Single(lib.Words);
        Assert.Equal("绝对化用语", lib.Words[0].Text);
    }

    [Fact]
    public void Add_empty_or_whitespace_text_is_rejected()
    {
        var editor = new WordLibraryEditor(new WordLibrary());

        Assert.Equal(AddWordResult.EmptyText, editor.Add(new WordEntry { Text = "" }));
        Assert.Equal(AddWordResult.EmptyText, editor.Add(new WordEntry { Text = "   " }));
        Assert.Empty(editor.Library.Words);
    }

    [Fact]
    public void Add_duplicate_text_is_rejected_case_and_whitespace_insensitive()
    {
        var lib = new WordLibrary();
        var editor = new WordLibraryEditor(lib);

        Assert.Equal(AddWordResult.Success, editor.Add(new WordEntry { Text = "绝对化用语" }));
        // 仅大小写/首尾空白不同视为同一词
        Assert.Equal(AddWordResult.Duplicate, editor.Add(new WordEntry { Text = " 绝对化用语 " }));
        Assert.Single(lib.Words);
    }

    [Fact]
    public void Remove_existing_word_by_id_succeeds_and_missing_returns_false()
    {
        var editor = new WordLibraryEditor(new WordLibrary());
        var id = Guid.NewGuid();
        editor.Add(new WordEntry { Id = id, Text = "某词" });

        Assert.True(editor.Remove(id));
        Assert.Empty(editor.Library.Words);
        Assert.False(editor.Remove(id)); // 已删除，二次移除返回 false
    }

    [Fact]
    public void SetEnabled_toggles_flag_by_id()
    {
        var editor = new WordLibraryEditor(new WordLibrary());
        var id = Guid.NewGuid();
        editor.Add(new WordEntry { Id = id, Text = "某词", Enabled = true });

        Assert.True(editor.SetEnabled(id, false));
        Assert.False(editor.Library.Words[0].Enabled);

        Assert.True(editor.SetEnabled(id, true));
        Assert.True(editor.Library.Words[0].Enabled);

        Assert.False(editor.SetEnabled(Guid.NewGuid(), true)); // 不存在的 Id
    }

    [Fact]
    public void SetEnabledForAll_disables_every_word_and_reports_changed_count()
    {
        var lib = new WordLibrary
        {
            Words =
            {
                new WordEntry { Text = "a", Enabled = true },
                new WordEntry { Text = "b", Enabled = true },
                new WordEntry { Text = "c", Enabled = false },
            }
        };
        var editor = new WordLibraryEditor(lib);

        var changed = editor.SetEnabledForAll(false);

        Assert.Equal(2, changed); // c 原本就停用，不计
        Assert.All(lib.Words, w => Assert.False(w.Enabled));
    }

    [Fact]
    public void Export_stamps_updatedAt_and_roundtrips_words()
    {
        var lib = new WordLibrary();
        var editor = new WordLibraryEditor(lib);
        editor.Add(new WordEntry { Text = "导出词", Severity = Severity.High });

        var json = editor.Export();
        var back = WordLibrary.Load(json);

        Assert.Equal(1, back.Words.Count);
        Assert.Equal("导出词", back.Words[0].Text);
        Assert.NotEqual(DateTime.MinValue, back.UpdatedAt);
        Assert.True(back.UpdatedAt <= DateTime.UtcNow + TimeSpan.FromSeconds(1));
    }
}
