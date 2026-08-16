using System.IO;

namespace WordGuard.Studio.App;

/// <summary>
/// 应用真实根目录解析。单文件发布时程序会被解压到 %TEMP%/.net/... 后运行，
/// 此时 AppContext.BaseDirectory 指向临时目录而非 exe 所在目录，外部资源（web/、词库）会找不到。
/// 用 Environment.ProcessPath 取真实 exe 所在目录，回退到 AppContext.BaseDirectory。
/// </summary>
internal static class AppPaths
{
    public static string BaseDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppContext.BaseDirectory;
}
