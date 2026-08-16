using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WordGuard.Studio.App;

/// <summary>
/// WebView2 承载窗体基类：加载随包分发的本地 HTML（对齐 prototype 设计稿），
/// 提供 JS↔C# 双向消息桥。
///
/// - C# → JS：<see cref="PostToJs(string)"/>（JSON 字符串）
/// - JS → C#：HTML 内 <c>window.chrome.webview.postMessage(json)</c>，由 <see cref="OnJsMessage"/>
///   处理（子类实现 switch-case 分发）。
/// 消息格式统一为 <c>{ "type": "...", ...payload }</c>。
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

    /// <summary>子类收到 JS 消息后的处理入口（type 分发）。</summary>
    protected abstract void OnJsMessage(string json);

    /// <summary>向页面 JS 发送 JSON 消息（如注入词库数据）。</summary>
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

        _web.CoreWebView2InitializationCompleted += (_, args) =>
        {
            // 初始化失败（WebView2 运行时缺失/被组策略禁用等）：明确提示并关闭窗体，避免静默崩溃。
            if (!args.IsSuccess)
            {
                MessageBox.Show(this,
                    "WebView2 运行时初始化失败，无法加载界面。\n请安装 Microsoft Edge WebView2 运行时后重试。\n\n详情：" +
                    (args.InitializationException?.Message ?? "未知错误"),
                    "WordGuard Studio 启动失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            // 只允许加载本地文件（file:// 同目录），禁止外部导航
            _web.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            _web.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _web.CoreWebView2.WebMessageReceived += (_, a) => OnJsMessage(a.WebMessageAsJson);
        };
        _web.NavigationStarting += (_, args) =>
        {
            // 拦截外部 URL 导航（只放行本地 html 文件）
            if (!args.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                args.Cancel = true;
        };

        // 单文件发布时优先用 exe 真实目录定位 web/，回退 AppContext.BaseDirectory（临时解压目录）。
        var htmlPath = Path.Combine(AppPaths.BaseDirectory, "web", _htmlRelativePath);
        if (!File.Exists(htmlPath))
            htmlPath = Path.Combine(AppContext.BaseDirectory, "web", _htmlRelativePath);

        _web.Source = new Uri(htmlPath);
    }

    /// <summary>序列化辅助：小驼峰 JSON，供 PostToJs 使用。</summary>
    protected static string Json(object obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
}
