using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WordGuard.Client;

namespace WordGuard.Client.App;

/// <summary>
/// 基于 UIA（UI Automation）+ Win32 前台窗口的真实探测实现。
///
/// <para>与仅依赖 <c>AutomationElement.FocusedElement</c> 的旧实现不同，本实现：</para>
/// <list type="bullet">
///   <item>用 <c>GetForegroundWindow</c> 拿到真正的前台窗口句柄，再反查进程（更可靠地判定"用户在哪个软件打字"）；</item>
///   <item>优先取该窗口内拥有键盘焦点的控件（即正在输入的框）；若焦点控件文本为空，则遍历窗口树取首个非空的可写控件（Edit / Document）；</item>
///   <item>逐控件用 TextPattern / ValuePattern / Name 兜底读取文本，覆盖富文本框与普通输入框；</item>
///   <item>每一步独立 try/catch，任何控件/窗口异常都静默跳过，绝不拖垮 500ms 轮询。</item>
/// </list>
/// </summary>
public sealed class UiaWindowProbe : IWindowProbe
{
    public WindowSnapshot? Probe()
    {
        IntPtr hwnd;
        try { hwnd = GetForegroundWindow(); }
        catch { return null; }
        if (hwnd == IntPtr.Zero) return null;

        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return null;

        Process? proc;
        try { proc = Process.GetProcessById((int)pid); }
        catch { return null; }

        var exeName = (proc.ProcessName ?? "") + ".exe";
        var exePath = proc.MainModule?.FileName ?? "";

        AutomationElement? window;
        try { window = AutomationElement.FromHandle(hwnd); }
        catch { return null; }

        var title = Safe(() => window.Current.Name) ?? "";

        // 优先：前台窗口内拥有键盘焦点的控件（正在输入的框）
        var focus = FocusedWithin(window);
        var target = focus ?? FirstEditable(window);
        if (target is null) return null;

        var text = ReadText(target);
        if (string.IsNullOrEmpty(text)) return null;

        var ctx = hwnd.ToString("X") + "|" + RuntimeIdString(target);
        return new WindowSnapshot(exeName, exePath, title, text, ctx);
    }

    /// <summary>取桌面级焦点元素，若它属于 <paramref name="window"/> 则返回，否则 null。</summary>
    private static AutomationElement? FocusedWithin(AutomationElement window)
    {
        AutomationElement? focused;
        try { focused = AutomationElement.FocusedElement; }
        catch { return null; }
        if (focused is null) return null;

        // 向上回溯，确认焦点元素确实落在前台窗口内
        var e = focused;
        for (var i = 0; i < 24 && e is not null; i++)
        {
            if (e.Equals(window)) return focused;
            try { e = TreeWalker.ControlViewWalker.GetParent(e); }
            catch { break; }
        }
        return null;
    }

    /// <summary>遍历窗口控件树，返回首个非空的可写文本控件（Edit / Document）。</summary>
    private static AutomationElement? FirstEditable(AutomationElement window)
    {
        AutomationElement? best = null;
        try
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty,
                ControlType.Edit);
            var edit = window.FindFirst(TreeScope.Descendants, cond);
            if (edit is not null && !string.IsNullOrEmpty(ReadText(edit)))
                return edit;

            var docCond = new PropertyCondition(AutomationElement.ControlTypeProperty,
                ControlType.Document);
            var doc = window.FindFirst(TreeScope.Descendants, docCond);
            if (doc is not null && !string.IsNullOrEmpty(ReadText(doc)))
                return doc;

            best = edit ?? doc;
        }
        catch { }
        return best;
    }

    private static string? ReadText(AutomationElement el)
    {
        // 1. TextPattern（富文本框，如聊天输入框）
        try
        {
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var p) && p is TextPattern tp)
            {
                var t = tp.DocumentRange.GetText(-1);
                if (!string.IsNullOrEmpty(t)) return t;
            }
        }
        catch { }

        // 2. ValuePattern（普通输入框）
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern vpat)
            {
                var t = vpat.Current.Value;
                if (!string.IsNullOrEmpty(t)) return t;
            }
        }
        catch { }

        // 3. Name 兜底
        try
        {
            var name = el.Current.Name;
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }

        return null;
    }

    private static string RuntimeIdString(AutomationElement el)
    {
        try
        {
            var rid = el.GetRuntimeId();
            if (rid is null || rid.Length == 0) return "0";
            return string.Join("-", rid);
        }
        catch { return "0"; }
    }

    private static T? Safe<T>(Func<T> f)
    {
        try { return f(); }
        catch { return default; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
