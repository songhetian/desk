using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 基于全局低级键盘钩子（WH_KEYBOARD_LL）的输入捕获器。
/// <para>作为 UIA 读不到文本时的兜底方案——维护一个"尽力而为"的输入缓冲区。</para>
/// <para>限制：</para>
/// <list type="bullet">
///   <item>中文输入法下只能拿到拼音字母，拿不到上屏后的中文（需要 IME 消息钩，需 DLL 注入）；</item>
///   <item>不知道光标位置、选中文本，假设总是在末尾追加/删除；</item>
///   <item>窗口切换时清空缓冲区（通过检测前台窗口变化）。</item>
/// </list>
/// </summary>
public sealed class KeyboardHookProbe : IWindowProbe, IDisposable
{
    private readonly InputBuffer _buffer = new();
    private readonly object _gate = new();
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private string _lastWindowKey = "";

    public Func<IReadOnlyCollection<string>>? TargetExesProvider { get; set; }

    public KeyboardHookProbe()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public WindowSnapshot? Probe()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        string exeName;
        string exePath;
        try
        {
            var proc = Process.GetProcessById((int)pid);
            exeName = (proc.ProcessName ?? "") + ".exe";
            exePath = proc.MainModule?.FileName ?? "";
        }
        catch { return null; }

        // 非目标窗口直接跳过（且清空缓冲区，避免串扰）
        if (TargetExesProvider is not null && !MonitorTargetPolicy.IsMonitored(exeName, TargetExesProvider()))
        {
            ClearBuffer();
            return null;
        }

        string text;
        lock (_gate)
        {
            text = _buffer.ToString();
        }

        if (string.IsNullOrEmpty(text)) return null;

        var title = "";
        try { title = GetWindowText(hwnd); } catch { }

        // 窗口标识（句柄值）变化时清空缓冲区，避免跨窗口串扰
        var windowKey = hwnd.ToString("X");
        if (_lastWindowKey != windowKey)
        {
            _lastWindowKey = windowKey;
            // 窗口切换：不清空已有内容（因为可能就是同一个框，句柄没变但窗口变了）
            // 保守起见还是留着，由 CaptureService 的"文本不变则不重复处理"去重
        }

        var ctx = "khook_" + windowKey;
        return new WindowSnapshot(exeName, exePath, title, text, ctx, IsPinyin: true);
    }

    /// <summary>手动清空缓冲区（测试或调试用）。</summary>
    public void ClearBuffer()
    {
        lock (_gate) _buffer.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var vk = Marshal.ReadInt32(lParam);
            HandleKeyDown((Keys)vk);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void HandleKeyDown(Keys key)
    {
        // 只处理感兴趣的键
        switch (key)
        {
            case Keys.Back:
                lock (_gate) _buffer.Backspace();
                break;
            case Keys.Return:
            case Keys.Tab:
            case Keys.Escape:
                // 回车提交 / Tab 切换框 / Esc 取消 —— 清空缓冲区
                lock (_gate) _buffer.Clear();
                break;
            default:
                // 尝试转成可打印字符
                var c = KeyToChar(key);
                if (c.HasValue)
                {
                    lock (_gate) _buffer.Append(c.Value);
                }
                break;
        }
    }

    /// <summary>把虚拟键码转成字符（只处理常见可打印键）。</summary>
    private static char? KeyToChar(Keys key)
    {
        // 字母（统一小写，实际区分大小写需要看 Shift 状态，这里简化处理）
        if (key >= Keys.A && key <= Keys.Z)
            return (char)('a' + (key - Keys.A));

        // 数字键（顶部行）
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));

        // 小键盘数字
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            return (char)('0' + (key - Keys.NumPad0));

        // 常见符号（简化）
        return key switch
        {
            Keys.Space => ' ',
            Keys.Oemcomma => ',',
            Keys.OemPeriod => '.',
            Keys.OemQuestion => '/',
            Keys.OemSemicolon => ';',
            Keys.OemQuotes => '\'',
            Keys.OemOpenBrackets => '[',
            Keys.OemCloseBrackets => ']',
            Keys.OemBackslash => '\\',
            Keys.OemMinus => '-',
            Keys.Oemplus => '=',
            Keys.Oemtilde => '`',
            _ => null,
        };
    }

    // ---- Win32 P/Invoke ----

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private static string GetWindowText(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }
}
