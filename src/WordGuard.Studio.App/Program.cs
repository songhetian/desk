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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, ex) => FatalError(ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            FatalError(ex.ExceptionObject as Exception ?? new Exception("未知非托管异常"));

        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var path = Path.Combine(AppPaths.BaseDirectory, LibraryFile);
            Application.Run(new MainForm(path));
        }
        catch (Exception ex)
        {
            FatalError(ex);
        }
    }

    private static void FatalError(Exception ex)
    {
        try
        {
            var log = Path.Combine(AppPaths.BaseDirectory, "studio-error.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }

        MessageBox.Show(
            null,
            "词库管理端启动失败：\n" + ex.Message +
            "\n\n如提示缺少 .NET 运行时，请前往下载安装：\n" +
            "https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0" +
            "\n\n详细信息已写入 studio-error.log（位于程序目录）。",
            "WordGuard Studio 启动失败",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        Environment.Exit(1);
    }
}
