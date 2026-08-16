using Microsoft.Web.WebView2.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 运行环境探测。自包含打包已把 .NET 运行时嵌入 exe，启动时真正可能缺失的是
/// 「Microsoft Edge WebView2 运行时」（系统级 Edge 组件，须单独安装）。
/// 缺它会导致所有 WebView2 窗体初始化失败、程序静默崩溃（此前"打不开也无提示"的根因之一）；
/// 因此在启动最早期真实探测，缺失时由 RuntimeMissingForm 引导安装。
/// </summary>
internal static class WebRuntime
{
    /// <summary>WebView2 运行时是否可用。任何探测异常都按"不可用"处理，宁可引导安装也不静默崩溃。</summary>
    public static bool IsWebView2Available()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            return false;
        }
    }
}
