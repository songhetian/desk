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

        var path = Path.Combine(AppContext.BaseDirectory, LibraryFile);
        Application.Run(new MainForm(path));
    }
}
