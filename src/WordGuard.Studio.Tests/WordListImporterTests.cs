using System.IO;
using System.IO.Compression;
using System.Text;
using WordGuard.Core;
using WordGuard.Studio;
using Xunit;

namespace WordGuard.Studio.Tests;

public class WordListImporterTests
{
    [Fact]
    public void ImportCsv_with_default_mapping_parses_text_category_severity()
    {
        // 默认映射：列0=文本, 列1=分类, 列2=严重度
        const string csv = "绝对化用语,夸大宣传,high\n免费送,诱导,medium\n";

        var result = new WordListImporter().ImportCsv(csv);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal("绝对化用语", result.Words[0].Text);
        Assert.Equal("夸大宣传", result.Words[0].Category);
        Assert.Equal(Severity.High, result.Words[0].Severity);
        Assert.Equal(Severity.Medium, result.Words[1].Severity);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ImportCsv_skips_blank_lines_and_flags_empty_text_rows()
    {
        // 第 2 行为空行（静默跳过），第 3 行文本仅空白（记为错误）
        const string csv = "绝对化用语,夸大宣传,high\n\n   ,分类,high\n";

        var result = new WordListImporter().ImportCsv(csv);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(ImportIssueKind.Error, result.Issues[0].Kind);
    }

    [Fact]
    public void ImportCsv_parses_severity_variants_and_defaults_unknown_to_medium()
    {
        const string csv = "高词,分类,高\n中词,分类,中\n低词,分类,low\n默词,分类,\n乱词,分类,xyz\n";

        var result = new WordListImporter().ImportCsv(csv);

        Assert.Equal(5, result.ImportedCount);
        Assert.Equal(Severity.High, result.Words[0].Severity);
        Assert.Equal(Severity.Medium, result.Words[1].Severity);
        Assert.Equal(Severity.Low, result.Words[2].Severity);
        Assert.Equal(Severity.Medium, result.Words[3].Severity); // 空白 → 中，无警告
        Assert.Equal(Severity.Medium, result.Words[4].Severity); // 无法识别 → 中，带警告
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void ImportCsv_parses_enabled_variants_and_defaults_to_true()
    {
        const string csv = "开词,分类,high,TRUE\n关词,分类,high,false\n空词,分类,high,\n否词,分类,high,否\n";

        var result = new WordListImporter().ImportCsv(csv);

        Assert.Equal(4, result.ImportedCount);
        Assert.True(result.Words[0].Enabled);
        Assert.False(result.Words[1].Enabled);
        Assert.True(result.Words[2].Enabled);  // 空白 → 启用
        Assert.False(result.Words[3].Enabled); // 「否」→ 停用
    }

    [Fact]
    public void ImportCsv_with_header_detects_columns_by_name()
    {
        const string csv = "word,分类,severity,enabled\n违禁A,诱导,high,true\n违禁B,夸大,中,false\n";

        var result = new WordListImporter().ImportCsv(csv, new ImportColumnMapping { HasHeader = true });

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal("违禁A", result.Words[0].Text);
        Assert.Equal("诱导", result.Words[0].Category);
        Assert.Equal(Severity.High, result.Words[0].Severity);
        Assert.True(result.Words[0].Enabled);
        Assert.False(result.Words[1].Enabled);
    }

    [Fact]
    public void ImportCsv_skips_duplicate_text_within_batch()
    {
        const string csv = "重复词,分类,high\n重复词,分类,high\n";

        var result = new WordListImporter().ImportCsv(csv);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains("重复", result.Issues[0].Message);
    }

    [Fact]
    public void ImportExcel_reads_xlsx_rows_into_words()
    {
        var path = Path.Combine(Path.GetTempPath(), "wordguard_import_test.xlsx");
        try
        {
            WriteMinimalXlsx(path, new[]
            {
                new[] { "绝对化用语", "夸大宣传", "high" },
                new[] { "免费送", "诱导", "medium" },
            });

            var result = new WordListImporter().ImportExcel(path);

            Assert.Equal(2, result.ImportedCount);
            Assert.Equal("绝对化用语", result.Words[0].Text);
            Assert.Equal(Severity.High, result.Words[0].Severity);
            Assert.Equal("诱导", result.Words[1].Category);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // 用最小合法 OOXML（inlineStr，免 sharedStrings）构造一个 .xlsx，验证 ExcelDataReader 集成。
    private static void WriteMinimalXlsx(string path, string[][] data)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(content);
        }

        Add("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>");
        Add("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Add("xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>");

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                  "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < data.Length; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < data[r].Length; c++)
            {
                var col = ColumnLetter(c);
                var val = new StringBuilder(data[r][c]);
                val.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                sb.Append($"<c r=\"{col}{r + 1}\" t=\"inlineStr\"><is><t>{val}</t></is></c>");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        Add("xl/worksheets/sheet1.xml", sb.ToString());
    }

    private static string ColumnLetter(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            s = (char)('A' + rem) + s;
            index = (index - 1) / 26;
        }
        return s;
    }
}
