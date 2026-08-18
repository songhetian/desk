using System.IO;
using OfficeOpenXml;
using WordGuard.Core;

namespace WordGuard.Studio;

/// <summary>
/// Excel 导入模板生成器。
/// 生成带表头、示例数据、说明的专业模板文件，便于管理员批量导入违禁词。
/// </summary>
public sealed class ExcelTemplateGenerator
{
    /// <summary>生成 Excel 导入模板并写入指定路径。</summary>
    public void Generate(string path)
    {
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("违禁词导入模板");

        // ---- 表头 ----
        var headers = new[] { "违禁词", "分类", "严重度", "是否启用" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            cell.Style.Border.BorderAround(OfficeOpenXml.Style.ExcelBorderStyle.Thin,
                System.Drawing.Color.FromArgb(229, 231, 235));
        }

        // ---- 示例数据 ----
        var samples = new (string Word, string Category, string Severity, string Enabled)[]
        {
            ("最低价", "价格违规", "高", "是"),
            ("绝对化用语", "夸大宣传", "中", "是"),
            ("保证效果", "诱导承诺", "高", "是"),
            ("加微信", "联系方式", "低", "是"),
        };
        for (int i = 0; i < samples.Length; i++)
        {
            var row = i + 2;
            ws.Cells[row, 1].Value = samples[i].Word;
            ws.Cells[row, 2].Value = samples[i].Category;
            ws.Cells[row, 3].Value = samples[i].Severity;
            ws.Cells[row, 4].Value = samples[i].Enabled;

            for (int c = 1; c <= 4; c++)
            {
                ws.Cells[row, c].Style.Border.BorderAround(OfficeOpenXml.Style.ExcelBorderStyle.Thin,
                    System.Drawing.Color.FromArgb(229, 231, 235));
            }
        }

        // ---- 列宽 ----
        ws.Column(1).Width = 20;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 12;

        // ---- 冻结首行 ----
        ws.View.FreezePanes(2, 1);

        // ---- 说明工作表 ----
        var ws2 = package.Workbook.Worksheets.Add("填写说明");
        ws2.Column(1).Width = 80;

        var notes = new[]
        {
            "违禁词导入模板 - 填写说明",
            "",
            "【列说明】",
            "A列 违禁词：必填，要监控的敏感词/违禁短语",
            "B列 分类：可选，用于分组管理，如「价格违规」「夸大宣传」",
            "C列 严重度：可选，高/中/低（默认「中」）",
            "D列 是否启用：可选，是/否（默认「是」）",
            "",
            "【严重度取值】",
            "  · 高：严重违规，立即弹窗告警 + 声音",
            "  · 中：中等违规，弹窗提示",
            "  · 低：轻微违规，仅记录日志",
            "",
            "【注意事项】",
            "  1. 第一行为表头，请勿删除或修改",
            "  2. 从第二行开始填写数据",
            "  3. 违禁词不能为空，空行会被自动跳过",
            "  4. 重复的违禁词会被自动去重",
            "  5. 保存为 .xlsx 格式后即可导入",
            "",
            "【支持的客服工具】",
            "  · 千牛（Qianniu）",
            "  · 京麦（Jingmai）",
            "  · 飞鸽（Feige）",
            "  · 微信（WeChat）",
            "  · QQ",
            "  · 钉钉（DingTalk）",
            "  · 企业微信",
        };

        for (int i = 0; i < notes.Length; i++)
        {
            var cell = ws2.Cells[i + 1, 1];
            cell.Value = notes[i];
            if (i == 0)
            {
                cell.Style.Font.Bold = true;
                cell.Style.Font.Size = 14;
            }
            else if (notes[i].StartsWith("【") && notes[i].EndsWith("】"))
            {
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            }
            cell.Style.WrapText = true;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        package.SaveAs(new FileInfo(path));
    }
}
