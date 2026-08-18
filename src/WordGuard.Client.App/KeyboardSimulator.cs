using System;
using System.Runtime.InteropServices;

namespace WordGuard.Client.App;

/// <summary>
/// 键盘输入模拟：使用 Win32 keybd_event 模拟按键，用于自动删除等场景。
/// </summary>
internal static class KeyboardSimulator
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_A = 0x41;
    private const byte VK_BACK = 0x08;
    private const byte VK_DELETE = 0x2E;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>
    /// 模拟 Ctrl+A 全选，然后 Backspace 删除。
    /// 调用前请确保目标输入框拥有焦点。
    /// </summary>
    public static void SelectAllAndDelete()
    {
        // Ctrl 按下
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        // A 按下
        keybd_event(VK_A, 0, 0, UIntPtr.Zero);
        // A 释放
        keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        // Ctrl 释放
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        // 稍微等一下，确保全选生效
        System.Threading.Thread.Sleep(10);

        // Backspace 按下并释放
        keybd_event(VK_BACK, 0, 0, UIntPtr.Zero);
        keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// 模拟 Delete 键（删除选中文本，备选方案）。
    /// </summary>
    public static void PressDelete()
    {
        keybd_event(VK_DELETE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_DELETE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
