using WordGuard.Client;
using Xunit;

namespace WordGuard.Client.Tests;

public class InputBufferTests
{
    [Fact]
    public void Appends_printable_characters()
    {
        var buf = new InputBuffer();
        buf.Append('a');
        buf.Append('b');
        buf.Append('c');

        Assert.Equal("abc", buf.ToString());
    }

    [Fact]
    public void Backspace_removes_last_character()
    {
        var buf = new InputBuffer();
        buf.Append('a');
        buf.Append('b');
        buf.Backspace();

        Assert.Equal("a", buf.ToString());
    }

    [Fact]
    public void Backspace_on_empty_does_nothing()
    {
        var buf = new InputBuffer();
        buf.Backspace();
        buf.Backspace();

        Assert.Equal("", buf.ToString());
    }

    [Fact]
    public void Clear_resets_buffer()
    {
        var buf = new InputBuffer();
        buf.Append('h');
        buf.Append('i');
        buf.Clear();

        Assert.Equal("", buf.ToString());
    }

    [Fact]
    public void Max_length_is_enforced()
    {
        var buf = new InputBuffer(maxLength: 5);
        buf.Append('1');
        buf.Append('2');
        buf.Append('3');
        buf.Append('4');
        buf.Append('5');
        buf.Append('6'); // 超了

        Assert.Equal(5, buf.Length);
        Assert.Equal("12345", buf.ToString());
    }

    [Fact]
    public void Chinese_characters_are_stored_as_is()
    {
        // 注意：低级键盘钩子通常拿不到中文（需要 IME 消息钩）
        // 但缓冲区本身支持 Unicode，如果通过其他途径拿到中文也能存
        var buf = new InputBuffer();
        buf.Append('退');
        buf.Append('货');

        Assert.Equal("退货", buf.ToString());
    }

    [Fact]
    public void Length_property_matches_content()
    {
        var buf = new InputBuffer();
        Assert.Equal(0, buf.Length);

        buf.Append('x');
        Assert.Equal(1, buf.Length);

        buf.Backspace();
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Has_content_is_true_when_non_empty()
    {
        var buf = new InputBuffer();
        Assert.False(buf.HasContent);

        buf.Append('a');
        Assert.True(buf.HasContent);

        buf.Clear();
        Assert.False(buf.HasContent);
    }
}
