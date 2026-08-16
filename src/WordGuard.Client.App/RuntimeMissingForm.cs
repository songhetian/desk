using System.Diagnostics;
using System.Windows.Forms;

namespace WordGuard.Client.App;

/// <summary>
/// 运行环境缺失引导对话框（PRD 用户故事 39）：本机缺少所需运行时时，启动即弹出，
/// 一句话说明 + 「前往安装」直达官方页、「退出」关闭。正常情况（已装运行时）不出现，仅作兜底。
/// </summary>
public sealed class RuntimeMissingForm : Form
{
    public RuntimeMissingForm(string environmentName, string installUrl)
    {
        Text = "需要安装运行环境";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 200);

        var label = new Label
        {
            Left = 20, Top = 20, Width = 370, Height = 80,
            Text = $"本程序需要「{environmentName}」才能运行。\n请点击「前往安装」打开官方下载页完成安装，然后重新启动本程序。",
        };
        Controls.Add(label);

        var installBtn = new Button
        {
            Text = "前往安装", Left = 20, Top = 120, Width = 120, Height = 35,
            DialogResult = DialogResult.OK,
        };
        installBtn.Click += (_, _) => Process.Start(new ProcessStartInfo(installUrl) { UseShellExecute = true });
        Controls.Add(installBtn);

        var exitBtn = new Button
        {
            Text = "退出", Left = 160, Top = 120, Width = 120, Height = 35,
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(exitBtn);

        AcceptButton = installBtn;
        CancelButton = exitBtn;
    }
}
