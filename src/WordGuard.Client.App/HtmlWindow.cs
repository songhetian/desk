using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WordGuard.Client.App;

/// <summary>
/// 客户端 WebView2 承载窗体基类：加载随包分发的本地 HTML（对齐 prototype 设计稿），
/// 提供 JS↔C# 双向消息桥。消息格式统一为 { "type": "...", ...payload }。
/// </summary>
public abstract class HtmlWindow : Form
{
    private WebView2 _web = null!;
    private readonly string _htmlRelativePath;

    protected HtmlWindow(string htmlRelativePath)
    {
        _htmlRelativePath = htmlRelativePath;
        Text = "WordGuard";
        StartPosition = FormStartPosition.CenterScreen;
        TrySetAppIcon();
    }

    /// <summary>从 exe 自身提取应用图标（ApplicationIcon），保证窗体/任务栏显示真实图标而非默认空白。</summary>
    protected void TrySetAppIcon()
    {
        try
        {
            var exe = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var ic = Icon.ExtractAssociatedIcon(exe);
                if (ic is not null) Icon = ic;
            }
        }
        catch { /* 提取失败不影响功能 */ }
    }

    /// <summary>子类可重写：悬浮球等需要真正透明背景的窗体返回 true，其余（状态/日志面板）保持不透明。</summary>
    protected virtual bool TransparentBackground => false;

    protected abstract void OnJsMessage(string json);

    protected void PostToJs(string json)
    {
        if (_web is not null && _web.CoreWebView2 is not null)
            _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);

        // 透明背景（仅悬浮球启用）：让 HTML 的透明区域真正透出桌面，配合圆形 Region 实现点击穿透。
        // 不透明窗体保持默认白底，避免状态/日志面板加载时闪白。
        try { _web.DefaultBackgroundColor = TransparentBackground ? Color.Transparent : Color.White; }
        catch { /* 旧版 WebView2 不支持该属性时忽略，退化为不透明 */ }

        _web.CoreWebView2InitializationCompleted += (_, args) =>
        {
            // 初始化失败（WebView2 运行时缺失/被组策略禁用等）：交给子类决定如何处理
            // （默认弹窗并关闭；悬浮球会重写以降级为 GDI 手绘，保证程序仍能打开）。
            if (!args.IsSuccess)
            {
                OnWebView2Failed(
                    "WebView2 运行时初始化失败，无法加载界面。\n请安装 Microsoft Edge WebView2 运行时后重试。\n\n详情：" +
                    (args.InitializationException?.Message ?? "未知错误"));
                return;
            }
            _web.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _web.CoreWebView2.WebMessageReceived += (_, a) => OnJsMessage(a.WebMessageAsJson);
        };
        _web.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                args.Cancel = true;
        };

        // 单文件发布时 AppContext.BaseDirectory 指向临时解压目录，外部 web 资源在 exe 实际目录下，
        // 故优先用 AppPaths.BaseDirectory（exe 真实目录），回退到 BaseDirectory 兜底。
        var htmlPath = Path.Combine(AppPaths.BaseDirectory, "web", _htmlRelativePath);
        if (!File.Exists(htmlPath))
            htmlPath = Path.Combine(AppContext.BaseDirectory, "web", _htmlRelativePath);
        _web.Source = new Uri(htmlPath);
    }

    protected static string Json(object obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });

    /// <summary>
    /// WebView2 初始化失败时的兜底钩子。基类默认：弹明确错误并关闭窗体（避免 _web.CoreWebView2 为 null 后续崩溃）。
    /// 子类可重写（如悬浮球重写以降级为 GDI 手绘，保证程序仍能打开而不直接退出）。
    /// </summary>
    protected virtual void OnWebView2Failed(string message)
    {
        MessageBox.Show(this, message, "WordGuard 启动失败",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    /// <summary>隐藏 WebView2 控件（降级为 GDI 手绘等场景使用）。</summary>
    protected void DisableWebView() { if (_web is not null) _web.Visible = false; }
}
