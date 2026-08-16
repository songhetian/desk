using System.IO;
using System.Windows.Forms;

namespace WordGuard.Studio.App;

/// <summary>词库编辑工具入口（独立桌面程序，非服务）。默认读写同目录 wordlib.json。</summary>
internal static class Program
{
    private const string LibraryFile = "wordlib.json";

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 运行环境缺失兜底：自带 .NET 运行时，真正可能缺的是 WebView2 运行时（系统级组件）。
        if (!WebRuntime.IsWebView2Available())
        {
            Application.Run(new RuntimeMissingForm("Microsoft Edge WebView2 运行时",
                "https://developer.microsoft.com/zh-cn/microsoft-edge/webview2/"));
            return;
        }

        var path = Path.Combine(AppPaths.BaseDirectory, LibraryFile);
        Application.Run(new MainForm(path));
    }
}
