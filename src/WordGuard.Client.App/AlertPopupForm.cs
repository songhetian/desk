using System.Drawing;
using System.Windows.Forms;
using WordGuard.Client;
using WordGuard.Core;

namespace WordGuard.Client.App;

/// <summary>
/// 告警弹窗（PRD：弹窗含「确认」按钮；非阻塞；60s 未确认记「未确认（超时）」）。
/// 用 RichTextBox 把触发内容中的违禁词标红（高亮仅限本工具界面，不覆盖目标软件窗口，见 ADR 0003）。
/// 确认/超时通过事件上抛，由捕获宿主统一写审计日志与去重确认。
/// </summary>
public sealed class AlertPopupForm : Form
{
    /// <summary>用户点击「确认」。</summary>
    public event Action? Confirmed;

    /// <summary>60 秒超时未确认。</summary>
    public event Action? TimedOut;

    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 60_000 };

    public AlertPopupForm(AlertEvent evt, string content, string target, string windowTitle)
    {
        Text = "违禁词提醒";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(420, 280);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.GetWorkingArea(Point.Empty);
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);

        var header = new Label
        {
            Left = 12, Top = 10, Width = 380,
            Text = $"在 {target} 检测到 {evt.AlertWords.Count} 个违禁词（{SeverityText(evt.TopSeverity)}）",
            Font = new Font(Font, FontStyle.Bold),
        };
        Controls.Add(header);

        var box = new RichTextBox
        {
            Left = 12, Top = 40, Width = 380, Height = 130,
            ReadOnly = true, Text = content,
            BackColor = Color.White,
        };
        Highlight(box, evt.AlertWords);
        Controls.Add(box);

        var channels = new Label
        {
            Left = 12, Top = 178, Width = 380,
            Text = "触发通道：" + string.Join("、",
                evt.Channels.Select(c => c switch
                {
                    AlertChannel.Popup => "弹窗",
                    AlertChannel.Sound => "声音",
                    AlertChannel.Highlight => "高亮",
                    _ => c.ToString(),
                })),
        };
        Controls.Add(channels);

        var confirm = new Button
        {
            Left = 300, Top = 214, Width = 92, Height = 32, Text = "我已知晓（确认）",
            BackColor = Color.FromArgb(70, 130, 255), ForeColor = Color.White,
        };
        confirm.Click += (_, _) => { _timeout.Stop(); Confirmed?.Invoke(); Close(); };
        Controls.Add(confirm);

        _timeout.Tick += (_, _) => { _timeout.Stop(); TimedOut?.Invoke(); Close(); };
        _timeout.Start();
    }

    private static void Highlight(RichTextBox box, IReadOnlyList<string> words)
    {
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        foreach (var w in words)
        {
            if (string.IsNullOrEmpty(w)) continue;
            var idx = 0;
            while ((idx = box.Text.IndexOf(w, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                box.SelectionStart = idx;
                box.SelectionLength = w.Length;
                box.SelectionColor = Color.Red;
                box.SelectionBackColor = Color.FromArgb(255, 255, 200, 200);
                idx += w.Length;
            }
        }
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        box.SelectionColor = Color.Black;
    }

    private static string SeverityText(Severity s) => s switch
    {
        Severity.High => "高",
        Severity.Medium => "中",
        _ => "低",
    };

    protected override void OnFormClosed(FormClosedEventArgs e) => _timeout.Dispose();
}
