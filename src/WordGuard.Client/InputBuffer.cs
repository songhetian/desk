using System.Text;

namespace WordGuard.Client;

/// <summary>
/// 键盘输入缓冲区：维护当前输入框的文本内容（尽力而为的近似）。
/// <para>注意：低级键盘钩子只能拿到按键序列，不知道光标位置、选中文本、输入法状态。
/// 因此缓冲区假设用户总是在末尾追加/删除，是一个不精确但够用的近似。</para>
/// <para>用于 UIA 读不到文本时的兜底方案——能检测英文/数字/符号违禁词，中文需依赖 IME 钩子或 UIA。</para>
/// </summary>
public sealed class InputBuffer
{
    private readonly StringBuilder _sb = new();
    private readonly int _maxLength;

    public const int DefaultMaxLength = 500;

    public int Length => _sb.Length;
    public bool HasContent => _sb.Length > 0;

    public InputBuffer() : this(DefaultMaxLength) { }

    public InputBuffer(int maxLength)
    {
        _maxLength = maxLength > 0 ? maxLength : DefaultMaxLength;
    }

    /// <summary>追加一个字符。超过最大长度时忽略。</summary>
    public void Append(char c)
    {
        if (_sb.Length >= _maxLength) return;
        _sb.Append(c);
    }

    /// <summary>退格：删除最后一个字符。空时无操作。</summary>
    public void Backspace()
    {
        if (_sb.Length == 0) return;
        _sb.Length--;
    }

    /// <summary>清空缓冲区（输入框切换、提交等场景）。</summary>
    public void Clear() => _sb.Clear();

    /// <summary>返回缓冲区当前内容。</summary>
    public override string ToString() => _sb.ToString();
}
