using System.IO;

namespace WordGuard.Client.App;

/// <summary>
/// 应用真实根目录解析。
///
/// <para>单文件发布（<c>dotnet publish --self-contained -p:PublishSingleFile=true</c>）时，
/// 程序会被解压到 <c>%TEMP%/.net/&lt;app&gt;/...</c> 后再运行，
/// 此时 <see cref="AppContext.BaseDirectory"/> 指向<b>临时解压目录</b>而非 exe 实际所在目录，
/// 随包分发的外部资源（<c>web/</c> 下的 HTML、<c>wordguard.settings.json</c>、生成的 <c>wordlib.json</c>、
/// <c>audit.db</c>）会全部找不到，导致悬浮球/状态/日志面板白屏、配置与词库读取失败。</para>
///
/// <para>用 <see cref="Environment.ProcessPath"/> 取 exe 真实路径的目录，可同时兼容：
/// 单文件发布（指向 exe 实际目录）、普通发布（同 AppContext.BaseDirectory）、调试（bin 目录）。
/// 仅在 ProcessPath 不可用时回退到 AppContext.BaseDirectory。</para>
/// </summary>
internal static class AppPaths
{
    /// <summary>应用根目录：exe 实际所在目录（单文件/普通/调试通用）。</summary>
    public static string BaseDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppContext.BaseDirectory;
}
