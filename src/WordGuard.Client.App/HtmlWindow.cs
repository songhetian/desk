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
    }

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

        _web.CoreWebView2InitializationCompleted += (_, _) =>
        {
            _web.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _web.CoreWebView2.WebMessageReceived += (_, args) => OnJsMessage(args.WebMessageAsJson);
        };
        _web.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                args.Cancel = true;
        };

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "web", _htmlRelativePath);
        if (!File.Exists(htmlPath))
            htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", _htmlRelativePath);
        _web.Source = new Uri(htmlPath);
    }

    protected static string Json(object obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
}
